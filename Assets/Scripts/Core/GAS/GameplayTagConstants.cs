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

        public static readonly GameplayTag GhostForm     = new(22);
        public static readonly GameplayTag BladeSheathed = new(23);
        public static readonly GameplayTag BladeDrawn    = new(24);
        public static readonly GameplayTag TimeStop      = new(25);
        public static readonly GameplayTag FreeTravel    = new(26);
        public static readonly GameplayTag Bound         = new(27);
        public static readonly GameplayTag BoundStrong   = new(28);
        public static readonly GameplayTag LifeFire      = new(29);
        public static readonly GameplayTag KnightSpirit  = new(30);
        public static readonly GameplayTag DivineProtect = new(31);
        public static readonly GameplayTag JudgeMode     = new(32);
        public static readonly GameplayTag NoStatus      = new(33);
        public static readonly GameplayTag GuardLight    = new(34);

        // ---- P7-A：双子附灵 / 数据幽灵形态（35-46）----
        public static readonly GameplayTag TwinVampiric = new(35); // 双子附灵·泣血状态
        public static readonly GameplayTag TwinAgile    = new(36); // 双子附灵·灵巧状态
        public static readonly GameplayTag V1Form       = new(37); // 数据幽灵当前形态 V1 音刃
        public static readonly GameplayTag V2Form       = new(38); // 数据幽灵当前形态 V2 长刃
        public static readonly GameplayTag V3Form       = new(39); // 数据幽灵当前形态 V3 三尖戟
        public static readonly GameplayTag V4Form       = new(40); // 数据幽灵当前形态 V4 花
        public static readonly GameplayTag V5Form       = new(41); // 数据幽灵当前形态 V5 恶魔猎弩
        public static readonly GameplayTag FormSwitched = new(42); // 本回合已切换过形态（回合末重置）
        public static readonly GameplayTag V2Unlocked   = new(43); // 规诫解锁（永久）：V2
        public static readonly GameplayTag V3Unlocked   = new(44); // 规诫解锁（永久）：V3
        public static readonly GameplayTag V4Unlocked   = new(45); // 规诫解锁（永久）：V4
        public static readonly GameplayTag V5Unlocked   = new(46); // 规诫解锁（永久）：V5
    }
}
