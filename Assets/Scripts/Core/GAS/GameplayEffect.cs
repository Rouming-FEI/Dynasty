namespace Core.GAS
{
    public enum GameplayEffectDurationType : byte
    {
        Instant,
        HasDuration
    }

    /// <summary>GE definition — template/config data. Created once at init, referenced by specs.</summary>
    public class GameplayEffect
    {
        public int Id;
        public GameplayEffectDurationType DurationType;
        public int DurationTurns;
        public AttributeModifier[] Modifiers;
        public GameplayTag[] GrantTags;
        public GameplayTag[] RemoveTags;
        public GameplayTag[] ApplicationRequiredTags;
        public GameplayTag[] ApplicationBlockedTags;
        public GameplayEffectPeriod? Period;            // null = no periodic execution
        public GameplayEffectStacking Stacking;         // default None
        public int CooldownTurns;                       // 0 = no cooldown
        public GameplayEffectExpiryPolicy ExpiryPolicy; // default RemoveOnExpiry
        public GameplayTag[] ConditionTags;  // RemoveOnCondition: owner 必须持有全部标签，任一缺失则移除

        public bool IsInstant => DurationType == GameplayEffectDurationType.Instant;
    }
}
