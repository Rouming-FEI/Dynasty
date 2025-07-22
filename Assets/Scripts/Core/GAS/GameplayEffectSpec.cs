namespace Dynasty.Core
{
    /// <summary>GE runtime instance — pooled, zero-GC via GenericPool.</summary>
    public class GameplayEffectSpec
    {
        public GameplayEffect Definition;
        public int RemainingTurns;
        public float MagnitudeMultiplier;
        public int SourceId;
        public int TargetId;

        public void Tick()
        {
            if (Definition != null && Definition.DurationType == GameplayEffectDurationType.HasDuration && RemainingTurns > 0)
                RemainingTurns--;
        }

        public bool IsExpired =>
            Definition != null &&
            Definition.DurationType == GameplayEffectDurationType.HasDuration &&
            RemainingTurns <= 0;

        public void Reset()
        {
            Definition = null;
            RemainingTurns = 0;
            MagnitudeMultiplier = 1f;
            SourceId = 0;
            TargetId = 0;
        }
    }
}
