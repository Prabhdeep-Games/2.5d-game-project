using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheSpaceBetween.ClothTrial
{
    /// <summary>
    /// A trial of Unity Cloth on the Protagonist's jacket hem, kept entirely apart from the production files.
    /// Tools > Cloth Trial > Build Test Copies makes, inside Assets/ClothTrial:
    ///   animation_Trial_ClothTest.unity   a copy of animation_Trial whose character uses the prefab below
    ///   Protagonist_ClothTest.prefab      a copy of the Protagonist prefab with a Cloth on its Skinned Mesh Renderer
    ///   Protagonist_Mesh_ClothTest.asset  a copy of the character mesh, so the FBX import settings stay untouched
    /// Only a band of the jacket just above the hem may move (Max Swing at the bottom edge, fading to nothing at Free Top).
    /// Every other vertex has a max distance of 0, so it follows the skinning exactly. Four trigger capsules on the hips,
    /// lower spine and thighs keep the hem out of the body. Running it again rebuilds the copies from the originals.
    /// The production scene, prefab and FBX are only read, never changed.
    /// </summary>
    public static class ClothTrialSetup
    {
        public const string Folder = "Assets/ClothTrial";
        public const string TestScenePath = Folder + "/animation_Trial_ClothTest.unity";
        public const string TestPrefabPath = Folder + "/Protagonist_ClothTest.prefab";
        private const string TestMeshPath = Folder + "/Protagonist_Mesh_ClothTest.asset";
        private const string SourceScenePath = "Assets/Scenes/animation_Trial.unity";
        private const string SourcePrefabPath = "Assets/Prefabs/Characters/Protagonist.prefab";

        //==================== WHICH VERTICES MAY MOVE (bind pose, metres) =====================
        // The jacket's bottom edge is at 0.928 and joins the trouser waist at 0.926, which stays pinned.
        public const float HemBottom = 0.927f;
        public const float FreeTop = 1.03f;          // the jacket above this follows the skinning exactly
        public const float MaxSwing = 0.035f;        // how far the bottom edge may leave its skinned position
        public const float TorsoHalfWidth = 0.24f;   // keeps the hands and sleeves out of the free band

        //==================== COLLIDERS (radii in metres) =====================
        private const float TorsoRadius = 0.115f;
        private const float ThighRadius = 0.08f;

        [MenuItem("Tools/Cloth Trial/Build Test Copies")]
        public static void Build()
        {
            if (Enumerable.Range(0, SceneManager.sceneCount).Any(i => SceneManager.GetSceneAt(i).isDirty))
            {
                Debug.LogError("Cloth Trial: save or revert the open scenes first; this opens the test scene.");
                return;
            }

            string returnTo = SceneManager.GetActiveScene().path;
            if (!AssetDatabase.IsValidFolder(Folder))
            {
                AssetDatabase.CreateFolder("Assets", "ClothTrial");
            }

            foreach (string path in new[] { TestScenePath, TestPrefabPath, TestMeshPath })
            {
                AssetDatabase.DeleteAsset(path);
            }

            AssetDatabase.CopyAsset(SourceScenePath, TestScenePath);
            AssetDatabase.CopyAsset(SourcePrefabPath, TestPrefabPath);

            BuildPrefab();
            PointSceneAtTestPrefab();

            if (!string.IsNullOrEmpty(returnTo))
            {
                EditorSceneManager.OpenScene(returnTo, OpenSceneMode.Single);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"Cloth Trial: built {TestScenePath} and {TestPrefabPath}.");
        }

        //==================== PREFAB =====================
        private static void BuildPrefab()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(TestPrefabPath);
            try
            {
                SkinnedMeshRenderer skin = root.GetComponentInChildren<SkinnedMeshRenderer>();
                Mesh mesh = Object.Instantiate(skin.sharedMesh);
                mesh.name = "Protagonist_Mesh_ClothTest";
                AssetDatabase.CreateAsset(mesh, TestMeshPath);

                // The copy keeps its data on the CPU too (Read/Write), which the cloth and the trial's measurements need
                // in Play mode. The FBX's own import setting is left alone.
                var serialized = new SerializedObject(mesh);
                serialized.FindProperty("m_IsReadable").boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssetIfDirty(mesh);
                skin.sharedMesh = mesh;

                // The prefab's bones are not in the bind pose while editing, so everything is placed in mesh space (the
                // bind pose) and carried into each bone's own space with the mesh's bind poses.
                float spineY = BindPosition(skin, "mixamorig:Spine1").y;
                CapsuleCollider torso = Capsule(skin, "Cloth Collider Torso", "mixamorig:Hips", new Vector3(0f, 0.86f, 0.01f), new Vector3(0f, spineY, 0.01f), TorsoRadius);
                CapsuleCollider leftThigh = Thigh(skin, "Left");
                CapsuleCollider rightThigh = Thigh(skin, "Right");

                Cloth cloth = skin.gameObject.AddComponent<Cloth>();
                Configure(cloth);
                cloth.capsuleColliders = new[] { torso, leftThigh, rightThigh };
                int free = SetCoefficients(cloth, mesh);

                PrefabUtility.SaveAsPrefabAsset(root, TestPrefabPath);
                Debug.Log($"Cloth Trial: {cloth.vertices.Length} cloth particles, {free} of them may move.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void Configure(Cloth cloth)
        {
            cloth.stretchingStiffness = 1f;      // no stretching beyond the rest length
            cloth.bendingStiffness = 0.25f;      // the hem may flare and fold a little
            cloth.damping = 0.2f;
            cloth.useGravity = true;
            cloth.friction = 0.3f;
            cloth.collisionMassScale = 0f;
            cloth.worldVelocityScale = 0.35f;    // how much running drags the hem back
            cloth.worldAccelerationScale = 0.7f; // how much starts, stops and landings kick it
            cloth.useTethers = true;
            cloth.clothSolverFrequency = 180f;
            cloth.sleepThreshold = 0.05f;
            cloth.enableContinuousCollision = true;
            cloth.useVirtualParticles = 0f;
            cloth.selfCollisionDistance = 0f;
            cloth.selfCollisionStiffness = 0f;
        }

        // The cloth particles are the mesh vertices welded by position. Unity reports them relative to the root bone, so
        // they are shifted back into mesh space (the bind pose) by the difference between the two bounding boxes.
        private static int SetCoefficients(Cloth cloth, Mesh mesh)
        {
            Vector3[] particles = cloth.vertices;
            var particleBounds = new Bounds(particles[0], Vector3.zero);
            foreach (Vector3 p in particles)
            {
                particleBounds.Encapsulate(p);
            }

            Vector3 toMesh = mesh.bounds.center - particleBounds.center;
            var coefficients = new ClothSkinningCoefficient[particles.Length];
            int free = 0;
            for (int i = 0; i < particles.Length; i++)
            {
                float swing = MaxDistanceAt(particles[i] + toMesh);
                coefficients[i].maxDistance = swing;
                coefficients[i].collisionSphereDistance = 0f; // the hem may never sink behind its skinned surface
                if (swing > 0f)
                {
                    free++;
                }
            }

            cloth.coefficients = coefficients;
            return free;
        }

        /// <summary>How far a vertex at this bind-pose position may leave its skinned position.</summary>
        public static float MaxDistanceAt(Vector3 p)
        {
            if (p.y < HemBottom || p.y > FreeTop || Mathf.Abs(p.x) > TorsoHalfWidth)
            {
                return 0f;
            }

            float t = Mathf.InverseLerp(FreeTop, HemBottom, p.y);
            return MaxSwing * t * t;
        }

        private static CapsuleCollider Thigh(SkinnedMeshRenderer skin, string side)
        {
            string upLeg = $"mixamorig:{side}UpLeg";
            return Capsule(skin, $"Cloth Collider {side} Thigh", upLeg, BindPosition(skin, upLeg), BindPosition(skin, $"mixamorig:{side}Leg"), ThighRadius);
        }

        // A trigger capsule from a to b (mesh space, bind pose) on the given bone, so it only talks to the cloth, never to
        // the movement (every movement query ignores triggers).
        private static CapsuleCollider Capsule(SkinnedMeshRenderer skin, string name, string boneName, Vector3 a, Vector3 b, float radius)
        {
            int bone = BoneIndex(skin, boneName);
            Matrix4x4 toBone = skin.sharedMesh.bindposes[bone];
            Vector3 la = toBone.MultiplyPoint3x4(a);
            Vector3 lb = toBone.MultiplyPoint3x4(b);
            float scale = toBone.MultiplyVector(Vector3.right).magnitude;

            var go = new GameObject(name);
            go.transform.SetParent(skin.bones[bone], false);
            go.transform.localPosition = (la + lb) * 0.5f;
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, lb - la);
            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.isTrigger = true;
            capsule.direction = 1;
            capsule.radius = radius * scale;
            capsule.height = Vector3.Distance(la, lb) + 2f * radius * scale;
            return capsule;
        }

        private static Vector3 BindPosition(SkinnedMeshRenderer skin, string boneName)
        {
            return skin.sharedMesh.bindposes[BoneIndex(skin, boneName)].inverse.MultiplyPoint3x4(Vector3.zero);
        }

        private static int BoneIndex(SkinnedMeshRenderer skin, string boneName)
        {
            return System.Array.FindIndex(skin.bones, b => b.name == boneName);
        }

        //==================== SCENE =====================
        private static void PointSceneAtTestPrefab()
        {
            Scene scene = EditorSceneManager.OpenScene(TestScenePath, OpenSceneMode.Single);
            var testPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TestPrefabPath);
            int replaced = 0;
            foreach (GameObject go in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject).ToArray())
            {
                if (go != null && PrefabUtility.IsAnyPrefabInstanceRoot(go)
                    && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go) == SourcePrefabPath)
                {
                    PrefabUtility.ReplacePrefabAssetOfPrefabInstance(go, testPrefab, InteractionMode.AutomatedAction);
                    replaced++;
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"Cloth Trial: {replaced} character instance(s) in the test scene now use {TestPrefabPath}.");
        }
    }
}
