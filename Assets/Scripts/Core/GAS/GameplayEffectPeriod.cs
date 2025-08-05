namespace Core.GAS
{
    /// <summary>Periodic execution config for a GE — fires every Interval turns.</summary>
    public struct GameplayEffectPeriod
    {
        public int Interval;      // execute once every N turns
        public int ExecuteCount;  // 0 = unlimited until effect expires

        public GameplayEffectPeriod(int interval, int executeCount = 0)
        {
            Interval = interval;
            ExecuteCount = executeCount;
        }
    }

    /// <summary>How re-applying the same GE on a target is resolved.</summary>
    public enum GameplayEffectStacking : byte
    {
        None,        // re-apply is ignored
        Aggregate,   // increment StackCount, refresh RemainingTurns
        Independent  // each application is a separate spec
    }

    /// <summary>How an active GE gets removed.</summary>
    public enum GameplayEffectExpiryPolicy : byte
    {
        RemoveOnExpiry,    // removed when RemainingTurns hits 0
        RemoveOnCondition  // removed by an external condition check
    }
}
