using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.TestTools;

namespace TheSpaceBetween.Tests
{
    public class GameEventTests : PlayModeTestBase
    {
        private GameEvent CreateEvent()
        {
            return Track(ScriptableObject.CreateInstance<GameEvent>());
        }

        private GameEventListener CreateListener(GameEvent gameEvent, UnityAction response)
        {
            GameEventListener listener = CreateInactive<GameEventListener>("Listener Test");
            SetField(listener, "gameEvent", gameEvent);

            var unityEvent = new UnityEvent();
            if (response != null)
            {
                unityEvent.AddListener(response);
            }

            SetField(listener, "response", unityEvent);
            listener.gameObject.SetActive(true);
            return listener;
        }

        [Test]
        public void Raise_IncrementsTheCountAndNotifiesSubscribers()
        {
            GameEvent gameEvent = CreateEvent();
            int notified = 0;
            gameEvent.Raised += () => notified++;

            gameEvent.Raise();
            gameEvent.Raise();

            Assert.AreEqual(2, gameEvent.RaiseCount);
            Assert.AreEqual(2, notified);
        }

        [Test]
        public void Listener_InvokesItsResponseWhenTheEventIsRaised()
        {
            GameEvent gameEvent = CreateEvent();
            int responses = 0;
            CreateListener(gameEvent, () => responses++);

            gameEvent.Raise();

            Assert.AreEqual(1, responses);
        }

        [Test]
        public void Listener_StopsRespondingWhenDisabled()
        {
            GameEvent gameEvent = CreateEvent();
            int responses = 0;
            GameEventListener listener = CreateListener(gameEvent, () => responses++);

            listener.enabled = false;
            gameEvent.Raise();

            Assert.AreEqual(0, responses);
        }

        [Test]
        public void Listener_WithoutAnEvent_LogsAWarning()
        {
            LogAssert.Expect(LogType.Warning, new Regex("has no GameEvent assigned"));

            Assert.DoesNotThrow(() => CreateListener(null, null));
        }
    }
}
