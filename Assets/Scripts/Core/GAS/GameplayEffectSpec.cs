namespace Core.GAS
{
    /// <summary>GE runtime instance — pooled, zero-GC via GenericPool.</summary>
    public class GameplayEffectSpec
    {
        public GameplayEffect Definition;
        public int RemainingTurns;
        public float MagnitudeMultiplier;
        public int SourceId;
        public int TargetId;
        public int PeriodTick;         // turns until next periodic execution
        public int PeriodRunCount;     // periodic executions done so far
        public int StackCount = 1;     // current stacks (default 1)
        public int CooldownRemaining;  // remaining cooldown turns

        /// <summary>
        /// Advance one turn. Returns true when the periodic execution fires
        /// this tick (caller applies the periodic payload).
        /// </summary>
        public bool Tick()
        {
            if (Definition == null)
                return false;

            if (Definition.DurationType == GameplayEffectDurationType.HasDuration && RemainingTurns > 0)
                RemainingTurns--;

            if (!Definition.Period.HasValue)
                return false;

            GameplayEffectPeriod period = Definition.Period.Value;

            // ExecuteCount 0 = unlimited until expiry.
            if (period.ExecuteCount > 0 && PeriodRunCount >= period.ExecuteCount)
                return false;

            if (PeriodTick > 0)
                PeriodTick--;

            if (PeriodTick > 0)
                return false;

            PeriodTick = period.Interval;
            PeriodRunCount++;
            return true;
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
            PeriodTick = 0;
            PeriodRunCount = 0;
            StackCount = 1;
            CooldownRemaining = 0;
        }
    }
}
