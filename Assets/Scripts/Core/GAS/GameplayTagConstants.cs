namespace Core.GAS
{
    /// <summary>
    /// 预定义的 GameplayTag 常量。ID 从 1 开始，每个标签占用 long 位掩码中的一位。
    /// </summary>
    public static class GameplayTagConstants
    {
        public static readonly GameplayTag Dead         = new(1);
        public static readonly GameplayTag Invulnerable = new(2);
        public static readonly GameplayTag CannotMove   = new(3);
        public static readonly GameplayTag CannotAct    = new(4);
        public static readonly GameplayTag HealBlocked  = new(5);
        public static readonly GameplayTag HasMoved     = new(6);

        public static readonly GameplayTag DamagePhysical  = new(7);
        public static readonly GameplayTag DamageMagic     = new(8);
        public static readonly GameplayTag DamageEffect    = new(9);
        public static readonly GameplayTag DamageTrue      = new(10);
        public static readonly GameplayTag ImmunePhysical  = new(11);
        public static readonly GameplayTag ImmuneMagic     = new(12);
        public static readonly GameplayTag ImmuneDamage    = new(13);
        public static readonly GameplayTag HasShield       = new(14);
        public static readonly GameplayTag MistState         = new(15);
        public static readonly GameplayTag ThornPotionActive = new(16);
        public static readonly GameplayTag DeathSaveActive   = new(17);
        public static readonly GameplayTag HideoutHidden     = new(18);
        public static readonly GameplayTag OnTurret          = new(19);
        public static readonly GameplayTag TurretLoaded      = new(20);
        public static readonly GameplayTag TurretAimed       = new(21);
    }
}
