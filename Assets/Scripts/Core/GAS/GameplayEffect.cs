namespace Dynasty.Core
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

        public bool IsInstant => DurationType == GameplayEffectDurationType.Instant;
    }
}
