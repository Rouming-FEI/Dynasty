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
        public SkillCost[] ExtraCosts;                    // 多段成本（主 Cost + 这些全部支付）
        public SkillRestriction[] Restrictions;            // 前置条件（全部满足才可施放）
        public int ChargeThreshold;                        // 充能技能阈值；0 = 非充能技能
    }
}
