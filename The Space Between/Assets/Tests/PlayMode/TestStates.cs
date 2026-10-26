namespace TheSpaceBetween.Tests
{
    /// <summary>A State whose eligibility is set by the test and which records every callback.</summary>
    public class RecordingState : State
    {
        public bool Eligible;
        public int EnterCount;
        public int TickCount;
        public int ExitCount;

        public override bool CanRun() => Eligible;

        public override void OnEnter() => EnterCount++;

        public override void Tick() => TickCount++;

        public override void OnExit() => ExitCount++;
    }
}
