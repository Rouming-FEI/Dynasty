namespace Core.GAS
{
    public class SkillDef
    {
        public int Id;
        public string Name;
        public SkillCost Cost;
        public SkillActivationType Activation;
        public GameplayEffect[] Effects; // GE applied when activated
        public int CooldownTurns;
        public GameplayTag[] RequiredTags;   // must have to activate
        public GameplayTag[] BlockingTags;   // cannot have to activate
    }
}
