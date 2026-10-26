using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace TheSpaceBetween.Tests
{
    public class StateMachineTests : PlayModeTestBase
    {
        private StateMachine machine;
        private RecordingState high;
        private RecordingState low;

        [SetUp]
        public void CreateMachine()
        {
            machine = CreateInactive<StateMachine>("Machine Test");
            high = machine.gameObject.AddComponent<RecordingState>();
            low = machine.gameObject.AddComponent<RecordingState>();
            SetField(machine, "states", new State[] { high, low });
            machine.gameObject.SetActive(true);
        }

        [UnityTest]
        public IEnumerator NoEligibleState_MeansNoActiveState()
        {
            yield return null;

            Assert.IsNull(machine.Current);
        }

        [UnityTest]
        public IEnumerator FirstEligibleStateInTheListWins()
        {
            high.Eligible = true;
            low.Eligible = true;

            yield return null;

            Assert.AreSame(high, machine.Current);
            Assert.AreEqual(1, high.EnterCount);
            Assert.AreEqual(0, low.EnterCount);
        }

        [UnityTest]
        public IEnumerator HigherPriorityStateInterruptsAndHandsBackWhenItEnds()
        {
            low.Eligible = true;
            yield return null;
            Assert.AreSame(low, machine.Current);

            high.Eligible = true;
            yield return null;
            Assert.AreSame(high, machine.Current);
            Assert.AreEqual(1, low.ExitCount);

            high.Eligible = false;
            yield return null;
            Assert.AreSame(low, machine.Current);
            Assert.AreEqual(1, high.ExitCount);
            Assert.AreEqual(2, low.EnterCount);
        }

        [UnityTest]
        public IEnumerator OnlyTheActiveStateTicks()
        {
            high.Eligible = true;
            low.Eligible = true;

            yield return null;
            yield return null;

            Assert.GreaterOrEqual(high.TickCount, 2);
            Assert.AreEqual(0, low.TickCount);
        }

        [UnityTest]
        public IEnumerator DisabledStatesAreSkipped()
        {
            high.Eligible = true;
            low.Eligible = true;
            high.enabled = false;

            yield return null;

            Assert.AreSame(low, machine.Current);
        }

        [UnityTest]
        public IEnumerator StateChanged_ReportsOldAndNewState()
        {
            State reportedFrom = high;
            State reportedTo = null;
            machine.StateChanged += (from, to) =>
            {
                reportedFrom = from;
                reportedTo = to;
            };
            low.Eligible = true;

            yield return null;

            Assert.IsNull(reportedFrom);
            Assert.AreSame(low, reportedTo);
        }

        [UnityTest]
        public IEnumerator DisablingTheMachine_ExitsTheActiveState()
        {
            high.Eligible = true;
            yield return null;

            machine.enabled = false;

            Assert.AreEqual(1, high.ExitCount);
            Assert.IsNull(machine.Current);
        }

        [UnityTest]
        public IEnumerator ChangeTo_SwitchesImmediately()
        {
            yield return null;

            machine.ChangeTo(low);

            Assert.AreSame(low, machine.Current);
            Assert.AreEqual(1, low.EnterCount);
        }
    }
}
