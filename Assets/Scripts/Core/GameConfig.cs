namespace Core
{
    public static class GameConfig
    {
        public const int MaxPlayers = 8;
        public const int ChipsPerPlayer = 3;
        public const int DefaultSanity = 3;
        public const int DefaultErosionLimit = 3;
        public const int MaxErosionLimit = 4;
        /// <summary>单批 ExecuteAll 最多执行的命令条数（批上限）。链式命令经队列 BFS 消费，
        /// 不是递归深度——递归深度限制由 EventBus.EventMaxDepth 承担。</summary>
        public const int CommandBatchLimit = 16;
        /// <summary>EventBus.Dispatch 递归深度上限；越限触发熔断（显式告警 + 计数，事件被丢弃）。</summary>
        public const int EventMaxDepth = 32;
        public const int MaxTurns = 999;
        public const int MaxLocations = 16;
    }
}
