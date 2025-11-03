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
        public SkillCost[] ExtraCosts;                    // 多段成本（主 Cost + 这些全部支付）；空元素（default struct）视为无成本，安全忽略
        public SkillRestriction[] Restrictions;            // 前置条件（全部满足才可施放）；空元素（default struct）视为无条件，安全忽略
        public int ChargeThreshold;                        // 充能技能阈值；0 = 非充能技能（SkillSystem 已接线，0 值兜底为 3）
        public int PassiveInterval;                     // 周期被动间隔（回合）；0 = 非周期被动
        public GameplayEffect[] PassiveEffects;         // 到期执行的负载
        public SkillJudgment Judgment;              // 非 null = 技能内判定
        public ReactiveTriggerType ReactiveTrigger; // Reactive 技能的触发点
    }

    /// <summary>响应式技能触发点。</summary>
    public enum ReactiveTriggerType : byte { None, OnPreDamage, OnPostDamage, OnTurnEnd, OnKill }

    /// <summary>技能内判定声明：对目标单对单判定，胜/败各绑定一套 GE。</summary>
    public class SkillJudgment
    {
        public GameplayEffect[] OnWinEffects;   // 胜利时对目标应用
        public GameplayEffect[] OnLoseEffects;  // 失败时对目标应用（null = 无事发生）
        public bool CancelOnLose;               // 失败时主 Effects 不应用
    }
}
