using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace TheSpaceBetween.Tests
{
    public class RaycastSensorTests : PlayModeTestBase
    {
        private const float SensorRange = 10f;

        private RaycastSensor sensor;

        [SetUp]
        public void CreateSensor()
        {
            sensor = CreateInactive<RaycastSensor>("Sensor Test");
            sensor.transform.position = Vector3.zero;
            SetField(sensor, "localDirection", Vector3.right);
            SetField(sensor, "maxDistance", SensorRange);
            sensor.gameObject.SetActive(true);
        }

        private GameObject CreateWall(float x)
        {
            GameObject wall = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            wall.name = "Wall Test";
            wall.transform.position = new Vector3(x, 0f, 0f);
            Physics.SyncTransforms();
            return wall;
        }

        [UnityTest]
        public IEnumerator NothingInFront_MeansNoDetection()
        {
            yield return null;

            Assert.IsFalse(sensor.HasDetections);
            Assert.IsFalse(sensor.IsHitting);
        }

        [UnityTest]
        public IEnumerator DetectsAWallAndReportsTheDistance()
        {
            GameObject wall = CreateWall(5f);

            yield return null;

            Assert.IsTrue(sensor.HasDetections);
            Assert.IsTrue(sensor.TryGetNearest(out Signal nearest));
            Assert.AreSame(wall, nearest.Target);
            Assert.AreEqual(4.5f, nearest.Distance, 0.01f);
        }

        [UnityTest]
        public IEnumerator IgnoresAWallBeyondTheMaxDistance()
        {
            CreateWall(SensorRange + 5f);

            yield return null;

            Assert.IsFalse(sensor.HasDetections);
        }

        [UnityTest]
        public IEnumerator RaisesDetectedAndLostAsTheWallAppearsAndGoes()
        {
            int detected = 0;
            int lost = 0;
            sensor.SignalAdded += _ => detected++;
            sensor.SignalLost += _ => lost++;

            GameObject wall = CreateWall(5f);
            yield return null;
            Assert.AreEqual(1, detected);

            Object.DestroyImmediate(wall);
            yield return null;

            Assert.AreEqual(1, lost);
            Assert.IsFalse(sensor.HasDetections);
        }

        [UnityTest]
        public IEnumerator ASolidObjectBlocksTheViewOfWhatIsBehindIt()
        {
            GameObject front = CreateWall(3f);
            CreateWall(7f);

            yield return null;

            Assert.AreEqual(1, sensor.Signals.Count);
            Assert.IsTrue(sensor.IsDetected(front));
        }

        [UnityTest]
        public IEnumerator DisablingTheSensor_ClearsItsDetections()
        {
            CreateWall(5f);
            yield return null;
            Assert.IsTrue(sensor.HasDetections);

            sensor.enabled = false;

            Assert.IsFalse(sensor.HasDetections);
        }
    }
}
