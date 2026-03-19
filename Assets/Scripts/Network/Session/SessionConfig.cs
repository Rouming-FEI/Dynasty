namespace Network.Session
{
    /// <summary>会话配置：对局规模、窗口时长、连接参数。
    /// 主机创建会话时填写，关键字段（seed/角色表/窗口时长）随 JoinAccept 下发客户端。</summary>
    public sealed class SessionConfig
    {
        public int PlayerCount = 4;          // 总槽位（真人+AI）
        public int RandomSeed = 42;
        public int JudgmentWindowMs = 30000; // 判定窗口
        public int ActionWindowMs = 20000;   // 行动窗口
        public int HeartbeatMs = 3000;
        public int ReconnectGraceMs = 60000; // 断线重连时限
        public int DisconnectTimeoutMs = 10000;
        public string ConnectKey = "dynasty-p6";
        public int MaxPeers = 8;
        public int[] HostPlayerDefIds;       // 主机槽位角色（长度=PlayerCount）
    }
}
