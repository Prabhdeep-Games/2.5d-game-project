using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TheSpaceBetween.ClothTrial
{
    /// <summary>
    /// Plays the cloth trial scene (Tools > Cloth Trial > Build Test Copies) through idle, walk, run, jump, sprint, a
    /// sudden stop, a turnaround, a high fall with the landing roll and a medium drop with a normal landing, with and
    /// without the Cloth. Every frame it compares each free hem particle with where the skinning alone would put it, and
    /// it measures stretch, sinking into the body, the leg and torso colliders, and how far the separate hem band and
    /// zipper drift off the jacket. It also renders close-ups of the hem and records the cost of the cloth.
    /// The report and pictures go to Temp/ClothTrial in the project folder. Explicit, so normal test runs skip it.
    /// </summary>
    [Explicit("Cloth trial; run on purpose.")]
    [Category("ClothTrial")]
    public class ClothTrialTests : InputTestFixture
    {
        private const string ScenePath = "Assets/ClothTrial/animation_Trial_ClothTest.unity";
        private const float BarrierX = 12f;

        private Keyboard keyboard;
        private Gamepad gamepad;
        private GameObject player;
        private PlaceholderPlayerController controller;
        private CharacterController capsule;
        private PlayerAnimationDriver driver;
        private SkinnedMeshRenderer skin;
        private Cloth cloth;
        private Camera shotCamera;
        private string outputFolder;
        private bool clothOn;
        private string variant;
        private readonly WaitForEndOfFrame endOfFrame = new WaitForEndOfFrame();

        // Particle data. Cloth particles are the mesh vertices welded by position, in order of first appearance.
        private int[] particleVertex;
        private Vector3[] meshVertices;
        private Vector3[] meshNormals;
        private BoneWeight[] boneWeights;
        private Matrix4x4[] bindposes;
        private Matrix4x4[] skinMatrices;
        private int[] free;
        private float[] freeMaxDistance;
        private int[] hemEdge;
        private (int a, int b)[] edges;
        private (int loose, int shell)[] attachments;
        private int[] shellFree;
        private int[] loosePieces;
        private int[] reference;  // pinned particles just below and above the free band: they show the measuring error
        private int spaceMode;
        private Vector3[] skinned;
        private Vector3[] skinnedNormal;

        private readonly List<(string name, ProfilerRecorder recorder)> recorders = new List<(string, ProfilerRecorder)>();
        private readonly List<Phase> phases = new List<Phase>();
        private Phase phase;

        private class Phase
        {
            public string Name;
            public int Frames;
            public float MaxDisplacement, HemEdgeDisplacementSum, MaxInward, MaxColliderDepth, MaxStretch, MaxElongation, MaxDrift, NoiseFloor;
            public int StretchedFrames, BrokenFrames;
            public double FrameMs, MainThreadMs, SolverMs;
            public readonly HashSet<string> States = new HashSet<string>();
        }

        public override void Setup()
        {
            base.Setup();
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            keyboard = InputSystem.AddDevice<Keyboard>();
            gamepad = InputSystem.AddDevice<Gamepad>();
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            foreach (var (_, recorder) in recorders)
            {
                recorder.Dispose();
            }

            recorders.Clear();
            if (shotCamera != null)
            {
                Object.Destroy(shotCamera.gameObject);
            }

            SceneManager.SetActiveScene(SceneManager.CreateScene("Empty After Cloth Trial"));
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            if (scene.isLoaded)
            {
                yield return SceneManager.UnloadSceneAsync(scene);
            }
        }

        // The jacket shell above the hem, the hem band and the lower zipper may all move.
        [UnityTest]
        public IEnumerator WithCloth()
        {
            yield return Run(true, false);
        }

        // Only the jacket shell may move; the separate hem band and zipper pieces stay on their skinning.
        [UnityTest]
        public IEnumerator WithCloth_ShellOnly()
        {
            yield return Run(true, true);
        }

        [UnityTest]
        public IEnumerator WithoutCloth()
        {
            yield return Run(false, false);
        }

        //==================== THE SEQUENCE =====================
        private IEnumerator Run(bool enableCloth, bool shellOnly)
        {
            clothOn = enableCloth;
            variant = !enableCloth ? "nocloth" : shellOnly ? "shell" : "cloth";
            phases.Clear();
            outputFolder = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/ClothTrial"));
            Directory.CreateDirectory(outputFolder);

#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#endif
            yield return null;
            player = GameObject.Find("Player");
            Assert.NotNull(player, "The cloth trial scene needs a Player. Run Tools > Cloth Trial > Build Test Copies.");
            controller = player.GetComponent<PlaceholderPlayerController>();
            capsule = player.GetComponent<CharacterController>();
            driver = player.GetComponent<PlayerAnimationDriver>();
            skin = player.GetComponentInChildren<SkinnedMeshRenderer>();
            cloth = skin.GetComponent<Cloth>();
            Assert.NotNull(cloth, "The trial character needs a Cloth.");
            cloth.enabled = enableCloth;
            StartRecorders();

            yield return Hold("settle", 0.6f, null);
            PrepareParticles();
            if (shellOnly)
            {
                PinLoosePieces();
                yield return Hold("settle pinned", 0.6f, null);
            }

            yield return Hold("idle", 2f, "idle");

            // Walk: the gamepad stick a third of the way, 2.1 u/s, the Walk clip.
            Set(gamepad.leftStick, new Vector2(0.35f, 0f));
            yield return Hold("walk", 2f, "walk");
            Set(gamepad.leftStick, Vector2.zero);
            yield return null;

            // Run at the move speed, then jump the barrier.
            Press(keyboard.dKey);
            yield return Until("run", () => player.transform.position.x >= BarrierX - 2.2f, 3f, 0.5f, "run");
            Press(keyboard.spaceKey);
            yield return null;
            Release(keyboard.spaceKey);
            yield return Until("jump", () => !controller.IsGrounded, 0.5f, -1f, null);
            yield return Until("jump", () => controller.Velocity.y <= 0f, 1f, 0f, "jump_apex");
            yield return Until("jump fall", () => controller.IsGrounded, 1.5f, -1f, null);
            yield return Hold("jump landing", 0.4f, "jump_land", 0.08f);

            // Sprint with Shift, then stop dead.
            Press(keyboard.leftShiftKey);
            yield return Until("sprint", () => player.transform.position.x >= 21f, 2f, 0.6f, "sprint");
            Release(keyboard.leftShiftKey);
            yield return null;
            Release(keyboard.dKey);
            yield return Hold("sudden stop", 1f, "stop", 0.12f);

            // Turn around and run back a little.
            Press(keyboard.aKey);
            yield return Hold("turnaround", 0.6f, "turn", 0.12f);
            Release(keyboard.aKey);
            yield return Hold("settle after turn", 0.6f, null);

            // A high fall: the landing roll.
            yield return Teleport(36f, 9f);
            yield return Until("high fall", () => controller.IsGrounded, 2f, 0.35f, "fall");
            yield return Hold("landing roll", 1.3f, "roll", 0.25f);

            // A medium drop: a normal landing.
            yield return Teleport(41f, 4.2f);
            yield return Until("medium drop", () => controller.IsGrounded, 1.5f, -1f, null);
            yield return Hold("normal landing", 0.8f, "land", 0.08f);

            yield return Hold("idle for timing", 2f, null);

            string report = Report();
            File.WriteAllText(Path.Combine(outputFolder, $"report_{variant}.txt"), report);
            Debug.Log(report);
            Assert.Pass(report);
        }

        private IEnumerator Teleport(float x, float y)
        {
            capsule.enabled = false;
            player.transform.position = new Vector3(x, y, player.transform.position.z);
            capsule.enabled = true;
            Physics.SyncTransforms();
            yield return null;
            if (clothOn)
            {
                cloth.ClearTransformMotion(); // a teleport is not movement the hem should react to
            }
        }

        // Records a phase for a fixed time, with one close-up after shotDelay seconds (or at the end).
        private IEnumerator Hold(string name, float seconds, string shot, float shotDelay = -1f)
        {
            Begin(name);
            float start = Time.time;
            bool shotTaken = shot == null;
            while (Time.time - start < seconds)
            {
                yield return endOfFrame;
                Measure();
                if (!shotTaken && shotDelay >= 0f && Time.time - start >= shotDelay)
                {
                    Shoot(shot);
                    shotTaken = true;
                }
            }

            if (!shotTaken)
            {
                Shoot(shot);
            }
        }

        // Records a phase until the condition holds, with one close-up after shotDelay seconds (0 means when it holds).
        private IEnumerator Until(string name, System.Func<bool> condition, float limit, float shotDelay, string shot)
        {
            if (phase == null || phase.Name != name)
            {
                Begin(name);
            }

            float start = Time.time;
            bool shotTaken = shot == null;
            while (!condition() && Time.time - start < limit)
            {
                yield return endOfFrame;
                Measure();
                if (!shotTaken && shotDelay > 0f && Time.time - start >= shotDelay)
                {
                    Shoot(shot);
                    shotTaken = true;
                }
            }

            if (!shotTaken)
            {
                Shoot(shot);
            }
        }

        private void Begin(string name)
        {
            phase = new Phase { Name = name };
            phases.Add(phase);
        }

        //==================== PARTICLES =====================
        private void PrepareParticles()
        {
            Mesh mesh = skin.sharedMesh;
            meshVertices = mesh.vertices;
            meshNormals = mesh.normals;
            boneWeights = mesh.boneWeights;
            bindposes = mesh.bindposes;
            skinMatrices = new Matrix4x4[bindposes.Length];
            int[] triangles = mesh.triangles;

            var ids = new Dictionary<Vector3Int, int>();
            var weld = new int[meshVertices.Length];
            var first = new List<int>();
            for (int i = 0; i < meshVertices.Length; i++)
            {
                Vector3Int key = Vector3Int.RoundToInt(meshVertices[i] * 10000f);
                if (!ids.TryGetValue(key, out int id))
                {
                    id = first.Count;
                    ids[key] = id;
                    first.Add(i);
                }

                weld[i] = id;
            }

            particleVertex = first.ToArray();
            int count = particleVertex.Length;
            Assert.AreEqual(cloth.vertices.Length, count, "The cloth particles should be the mesh vertices welded by position.");

            ClothSkinningCoefficient[] coefficients = cloth.coefficients;
            free = Enumerable.Range(0, count).Where(i => coefficients[i].maxDistance > 0f).ToArray();
            freeMaxDistance = free.Select(i => coefficients[i].maxDistance).ToArray();
            float largest = freeMaxDistance.Length > 0 ? freeMaxDistance.Max() : 0f;
            hemEdge = free.Where(i => coefficients[i].maxDistance >= largest * 0.95f).ToArray();
            skinned = new Vector3[count];
            skinnedNormal = new Vector3[count];

            // Edges touching a free particle, and which pieces of the mesh are separate from the jacket shell.
            var freeSet = new HashSet<int>(free);
            var edgeSet = new HashSet<(int, int)>();
            var neighbours = new List<int>[count];
            for (int i = 0; i < count; i++)
            {
                neighbours[i] = new List<int>();
            }

            for (int t = 0; t < triangles.Length; t += 3)
            {
                for (int k = 0; k < 3; k++)
                {
                    int a = weld[triangles[t + k]], b = weld[triangles[t + (k + 1) % 3]];
                    if (a == b)
                    {
                        continue;
                    }

                    neighbours[a].Add(b);
                    neighbours[b].Add(a);
                    if (freeSet.Contains(a) || freeSet.Contains(b))
                    {
                        edgeSet.Add(a < b ? (a, b) : (b, a));
                    }
                }
            }

            edges = edgeSet.ToArray();
            var component = Enumerable.Repeat(-1, count).ToArray();
            var sizes = new List<int>();
            for (int s = 0; s < count; s++)
            {
                if (component[s] >= 0)
                {
                    continue;
                }

                var stack = new Stack<int>();
                stack.Push(s);
                component[s] = sizes.Count;
                int size = 0;
                while (stack.Count > 0)
                {
                    int x = stack.Pop();
                    size++;
                    foreach (int y in neighbours[x].Where(y => component[y] < 0))
                    {
                        component[y] = sizes.Count;
                        stack.Push(y);
                    }
                }

                sizes.Add(size);
            }

            // The jacket shell is the biggest piece among the free particles; the rest (hem band, zipper) sit on it.
            int shell = free.GroupBy(i => component[i]).OrderByDescending(g => sizes[g.Key]).First().Key;
            shellFree = free.Where(i => component[i] == shell).ToArray();
            loosePieces = free.Where(i => component[i] != shell).ToArray();
            int[] loose = loosePieces;
            Vector3[] rest = particleVertex.Select(v => meshVertices[v]).ToArray();
            attachments = loose.Select(l => (l, shellFree.OrderBy(s => (rest[s] - rest[l]).sqrMagnitude).First())).ToArray();
            reference = Enumerable.Range(0, count).Where(i => !freeSet.Contains(i) && Mathf.Abs(rest[i].x) < 0.24f
                && ((rest[i].y > 0.90f && rest[i].y < 0.927f) || (rest[i].y > 1.03f && rest[i].y < 1.08f))).ToArray();

            // Which space Unity reports the cloth particles in, judged on the pinned particles.
            Skin(true);
            Vector3[] current = cloth.vertices;
            int[] pinned = Enumerable.Range(0, count).Where(i => !freeSet.Contains(i)).Where((_, n) => n % 50 == 0).ToArray();
            var errors = new float[3];
            for (int mode = 0; mode < 3; mode++)
            {
                spaceMode = mode;
                errors[mode] = pinned.Average(i => (ToWorld(current[i]) - skinned[i]).magnitude);
            }

            spaceMode = System.Array.IndexOf(errors, errors.Min());
            Debug.Log($"[Cloth Trial] {count} particles, {free.Length} free ({loose.Length} on the hem band and zipper), {edges.Length} edges; particle space {spaceMode} (pinned error {errors.Min() * 1000f:F1} mm; others {string.Join(", ", errors.Select(e => (e * 1000f).ToString("F0")))} mm)");
        }

        // Gives the hem band and zipper a max distance of 0, so only the jacket shell is simulated.
        private void PinLoosePieces()
        {
            ClothSkinningCoefficient[] coefficients = cloth.coefficients;
            foreach (int i in loosePieces)
            {
                coefficients[i].maxDistance = 0f;
            }

            cloth.coefficients = coefficients;
            free = shellFree;
            freeMaxDistance = free.Select(i => coefficients[i].maxDistance).ToArray();
            var freeSet = new HashSet<int>(free);
            edges = edges.Where(e => freeSet.Contains(e.a) || freeSet.Contains(e.b)).ToArray();
            hemEdge = hemEdge.Where(freeSet.Contains).ToArray();
        }

        private Vector3 ToWorld(Vector3 p)
        {
            switch (spaceMode)
            {
                case 0: return p;
                case 1: return skin.transform.TransformPoint(p);
                default: return skin.rootBone.TransformPoint(p);
            }
        }

        // Where the skinning alone puts each particle this frame (the cloth's own animated target).
        private void Skin(bool all = false)
        {
            Transform[] bones = skin.bones;
            for (int b = 0; b < bones.Length; b++)
            {
                skinMatrices[b] = bones[b].localToWorldMatrix * bindposes[b];
            }

            IEnumerable<int> wanted = all
                ? Enumerable.Range(0, particleVertex.Length)
                : free.Concat(edges.SelectMany(e => new[] { e.a, e.b })).Concat(reference).Concat(attachments.Select(x => x.shell)).Distinct();

            foreach (int i in wanted)
            {
                int v = particleVertex[i];
                BoneWeight w = boneWeights[v];
                Matrix4x4 m = Blend(w);
                skinned[i] = m.MultiplyPoint3x4(meshVertices[v]);
                skinnedNormal[i] = m.MultiplyVector(meshNormals[v]).normalized;
            }
        }

        private Matrix4x4 Blend(BoneWeight w)
        {
            Matrix4x4 m = new Matrix4x4();
            Add(ref m, skinMatrices[w.boneIndex0], w.weight0);
            Add(ref m, skinMatrices[w.boneIndex1], w.weight1);
            Add(ref m, skinMatrices[w.boneIndex2], w.weight2);
            Add(ref m, skinMatrices[w.boneIndex3], w.weight3);
            return m;
        }

        private static void Add(ref Matrix4x4 m, Matrix4x4 b, float w)
        {
            if (w <= 0f)
            {
                return;
            }

            for (int i = 0; i < 16; i++)
            {
                m[i] += b[i] * w;
            }
        }

        //==================== MEASURING =====================
        private void Measure()
        {
            phase.Frames++;
            phase.States.Add(driver.CurrentState);
            phase.FrameMs += Time.unscaledDeltaTime * 1000.0;
            foreach (var (name, recorder) in recorders)
            {
                if (name.StartsWith("ClothScene.SolverChunkJob"))
                {
                    phase.SolverMs += recorder.LastValue / 1e6;
                }
                else if (name.StartsWith("FixedUpdate.") || name.StartsWith("PostLateUpdate."))
                {
                    phase.MainThreadMs += recorder.LastValue / 1e6;
                }
            }

            if (!clothOn || free == null || free.Length == 0)
            {
                return;
            }

            Skin();
            Vector3[] raw = cloth.vertices;
            var world = new Dictionary<int, Vector3>();
            Vector3 At(int i)
            {
                if (!world.TryGetValue(i, out Vector3 p))
                {
                    p = ToWorld(raw[i]);
                    world[i] = p;
                }

                return p;
            }

            bool broken = false;
            for (int n = 0; n < free.Length; n++)
            {
                int i = free[n];
                Vector3 p = At(i);
                if (!float.IsFinite(p.x) || !float.IsFinite(p.y) || !float.IsFinite(p.z))
                {
                    broken = true;
                    continue;
                }

                Vector3 offset = p - skinned[i];
                phase.MaxDisplacement = Mathf.Max(phase.MaxDisplacement, offset.magnitude);
                phase.MaxInward = Mathf.Max(phase.MaxInward, -Vector3.Dot(offset, skinnedNormal[i]));
                if (offset.magnitude > freeMaxDistance[n] + 0.01f)
                {
                    broken = true;
                }

                foreach (CapsuleCollider c in cloth.capsuleColliders)
                {
                    phase.MaxColliderDepth = Mathf.Max(phase.MaxColliderDepth, Depth(c, p));
                }
            }

            phase.HemEdgeDisplacementSum += hemEdge.Length == 0 ? 0f : hemEdge.Average(i => (At(i) - skinned[i]).magnitude);
            foreach (int i in reference)
            {
                phase.NoiseFloor = Mathf.Max(phase.NoiseFloor, (At(i) - skinned[i]).magnitude);
            }

            // Edges shorter than 5 mm (the hem step, the band's and zipper's rounding) are left out of the ratio.
            float worst = 1f;
            foreach (var (a, b) in edges)
            {
                float restLength = (skinned[a] - skinned[b]).magnitude;
                float length = (At(a) - At(b)).magnitude;
                phase.MaxElongation = Mathf.Max(phase.MaxElongation, length - restLength);
                if (restLength >= 0.005f)
                {
                    worst = Mathf.Max(worst, length / restLength);
                }
            }

            phase.MaxStretch = Mathf.Max(phase.MaxStretch, worst);
            if (worst > 1.1f)
            {
                phase.StretchedFrames++;
            }

            foreach (var (loose, shell) in attachments)
            {
                float drift = Mathf.Abs((At(loose) - At(shell)).magnitude - (skinned[loose] - skinned[shell]).magnitude);
                phase.MaxDrift = Mathf.Max(phase.MaxDrift, drift);
            }

            if (broken)
            {
                phase.BrokenFrames++;
            }
        }

        // How deep a point sits inside a capsule collider, in metres (0 outside).
        private static float Depth(CapsuleCollider c, Vector3 p)
        {
            Transform t = c.transform;
            Vector3 scale = t.lossyScale;
            float radius = c.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            float half = Mathf.Max(c.height * Mathf.Abs(scale.y) * 0.5f - radius, 0f);
            Vector3 centre = t.TransformPoint(c.center);
            Vector3 axis = t.up;
            float along = Mathf.Clamp(Vector3.Dot(p - centre, axis), -half, half);
            return Mathf.Max(0f, radius - (p - (centre + axis * along)).magnitude);
        }

        private void StartRecorders()
        {
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            foreach (ProfilerRecorderHandle handle in handles)
            {
                ProfilerRecorderDescription description = ProfilerRecorderHandle.GetDescription(handle);
                bool clothMarker = description.Name.StartsWith("ClothScene.SolverChunkJob")
                                   || description.Name == "FixedUpdate.PhysicsClothFixedUpdate"
                                   || description.Name.StartsWith("PostLateUpdate.PhysicsSkinnedCloth");
                if (clothMarker && description.UnitType == ProfilerMarkerDataUnit.TimeNanoseconds)
                {
                    recorders.Add((description.Name, ProfilerRecorder.StartNew(description.Category, description.Name)));
                }
            }
        }

        //==================== PICTURES =====================
        private void Shoot(string label)
        {
            if (shotCamera == null)
            {
                shotCamera = new GameObject("Cloth Trial Camera").AddComponent<Camera>();
                shotCamera.fieldOfView = 26f;
                shotCamera.nearClipPlane = 0.05f;
                shotCamera.enabled = false;
            }

            // Aimed at the middle of the hem's bottom edge, where the skinning puts it this frame.
            Skin();
            Vector3 hem = Vector3.zero;
            foreach (int i in hemEdge)
            {
                hem += skinned[i] / hemEdge.Length;
            }

            Vector3 forward = driver.Animator.transform.forward;
            forward.y = 0f;
            forward.Normalize();

            Render(label + "_side", hem + new Vector3(0f, 0.05f, -2.1f), hem);
            Render(label + "_front", hem + forward * 1.5f + new Vector3(0f, 0.15f, -1.1f), hem);
        }

        private void Render(string name, Vector3 from, Vector3 at)
        {
            shotCamera.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from));
            var target = RenderTexture.GetTemporary(512, 512, 24, RenderTextureFormat.ARGB32);
            var request = new RenderPipeline.StandardRequest { destination = target };
            if (RenderPipeline.SupportsRenderRequest(shotCamera, request))
            {
                RenderPipeline.SubmitRenderRequest(shotCamera, request);
            }
            else
            {
                shotCamera.targetTexture = target;
                shotCamera.Render();
                shotCamera.targetTexture = null;
            }

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var picture = new Texture2D(512, 512, TextureFormat.RGB24, false);
            picture.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
            picture.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            File.WriteAllBytes(Path.Combine(outputFolder, $"{variant}_{name}.png"), picture.EncodeToPNG());
            Object.Destroy(picture);
        }

        //==================== REPORT =====================
        private string Report()
        {
            var text = new StringBuilder();
            text.AppendLine($"Cloth Trial, {variant}: {particleVertex.Length} particles, {free.Length} free, hem edge {hemEdge.Length}, loose pieces {attachments.Length}, recorders: {string.Join(", ", recorders.Select(r => r.name))}");
            text.AppendLine("phase                | frames | frame ms | cloth main ms | solver ms | error floor mm | max move mm | hem edge avg mm | max inward mm | collider depth mm | max stretch | frames >10% | max elongation mm | band/zip drift mm | over limit | states");
            foreach (Phase p in phases.Where(p => p.Frames > 0))
            {
                text.AppendLine($"{p.Name,-20} | {p.Frames,6} | {p.FrameMs / p.Frames,8:F2} | {p.MainThreadMs / p.Frames,13:F3} | {p.SolverMs / p.Frames,9:F3} | {p.NoiseFloor * 1000f,14:F1} | {p.MaxDisplacement * 1000f,11:F1} | {p.HemEdgeDisplacementSum / p.Frames * 1000f,15:F1} | {p.MaxInward * 1000f,13:F1} | {p.MaxColliderDepth * 1000f,17:F1} | {p.MaxStretch,11:F3} | {p.StretchedFrames,11} | {p.MaxElongation * 1000f,17:F1} | {p.MaxDrift * 1000f,17:F1} | {p.BrokenFrames,10} | {string.Join(" ", p.States)}");
            }

            return text.ToString();
        }
    }
}
