namespace Core.GAS
{
    public enum SkillCostType : byte { AP, Sanity, ErosionLimit, Charge }
    public enum SkillActivationType : byte { Active, Passive, Reactive }

    public struct SkillCost
    {
        public SkillCostType Type;
        public float Amount;
        public SkillCost(SkillCostType type, float amount) { Type = type; Amount = amount; }
    }

    /// <summary>技能前置条件：某属性当前值 >= MinValue 才可施放。</summary>
    public struct SkillRestriction
    {
        public int AttributeId;
        public float MinValue;
        public SkillRestriction(int attributeId, float minValue) { AttributeId = attributeId; MinValue = minValue; }
    }
}
