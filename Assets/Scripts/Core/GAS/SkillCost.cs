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
}
