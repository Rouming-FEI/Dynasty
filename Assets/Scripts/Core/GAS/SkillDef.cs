namespace Core.GAS
{
    /// <summary>技能目标类型：Self 只对自己施放（自 Buff），Enemy 对敌方，Any 任意角色。</summary>
    public enum SkillTargetType : byte { Enemy, Self, Any }

    public class SkillDef
    {
        public int Id;
        public string Name;
        public SkillCost Cost;
        public SkillActivationType Activation;
        public SkillTargetType Target;   // 默认 Enemy
        public GameplayEffect[] Effects; // GE applied when activated
        public int CooldownTurns;
        public GameplayTag[] RequiredTags;   // must have to activate
        public GameplayTag[] BlockingTags;   // cannot have to activate
    }
}
