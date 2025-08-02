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
    }
}
