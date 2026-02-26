using System;

namespace Network.Session
{
    /// <summary>会话阶段：Lobby（玩家加入/分配槽位）→ InGame → GameOver。</summary>
    public enum SessionPhase { Lobby, InGame, GameOver }

    /// <summary>
    /// 会话状态机：Lobby（玩家加入/分配槽位）→ InGame → GameOver。
    /// 主机侧持有 peerId→playerId 映射；槽位表只登记网络对端，主机本地玩家由 LocalPlayerId（=0）标识、不占槽——
    /// 因此 HandleHello 按空槽顺序分配 0..PlayerCount-1。断线重连窗口（ReconnectGraceMs）由 Task 11
    /// ReconnectHandler 接入；本层断线即释放槽位。纯 C# 状态机，不依赖真实 UDP。</summary>
    public sealed class NetworkSession
    {
        private readonly int[] _slotOwnerPeer;   // 槽位→peerId（-1=AI 槽/未连接）
        private int[] _playerDefIds;

        public SessionPhase Phase { get; private set; }
        public bool IsHost { get; }
        public int LocalPlayerId { get; private set; }   // 主机 = 0
        public int Seed { get; private set; }
        public int[] PlayerDefIds => _playerDefIds;      // 槽位角色表（长度=PlayerCount）
        public SessionConfig Config { get; }

        /// <summary>槽位→peerId（-1=AI 槽/未连接），主机视角；调用方只读勿改。</summary>
        public int[] PlayerPeerIds => _slotOwnerPeer;

        /// <summary>已连接真人槽数（网络对端占用数）。</summary>
        public int ConnectedPlayerCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _slotOwnerPeer.Length; i++)
                    if (_slotOwnerPeer[i] != -1) n++;
                return n;
            }
        }

        /// <summary>主机视角：新玩家入座（playerId）。</summary>
        public event Action<int> OnPlayerJoined;

        /// <summary>主机视角：玩家断线/离座（playerId）。</summary>
        public event Action<int> OnPlayerLeft;

        /// <summary>满员（主机开战信号）。</summary>
        public event Action OnAllReady;

        public NetworkSession(SessionConfig config, bool isHost, int localPlayerId = -1)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            if (config.PlayerCount <= 0)
                throw new ArgumentException("PlayerCount 必须为正数", nameof(config));
            if (isHost && (config.HostPlayerDefIds == null || config.HostPlayerDefIds.Length != config.PlayerCount))
                throw new ArgumentException("主机必须提供长度等于 PlayerCount 的 HostPlayerDefIds", nameof(config));

            Config = config;
            IsHost = isHost;
            Phase = SessionPhase.Lobby;
            Seed = config.RandomSeed;
            LocalPlayerId = isHost ? 0 : localPlayerId;
            _slotOwnerPeer = new int[config.PlayerCount];
            for (int i = 0; i < _slotOwnerPeer.Length; i++)
                _slotOwnerPeer[i] = -1;
            if (isHost)
            {
                _playerDefIds = new int[config.PlayerCount];
                Array.Copy(config.HostPlayerDefIds, _playerDefIds, config.PlayerCount);
            }
        }

        /// <summary>主机：处理 Hello（peerId, requestedDefId）→ 分配第一个空槽。
        /// 返回分配的 playerId（槽索引），-1=拒绝（满员或已开战）。角色协商由上层负责，本层按主机配置表分配。</summary>
        public int HandleHello(int peerId, int requestedDefId)
        {
            if (!IsHost)
                throw new InvalidOperationException("仅主机可处理加入请求");
            if (Phase != SessionPhase.Lobby)
                return -1;
            for (int i = 0; i < _slotOwnerPeer.Length; i++)
            {
                if (_slotOwnerPeer[i] == -1)
                {
                    _slotOwnerPeer[i] = peerId;
                    OnPlayerJoined?.Invoke(i);
                    if (ConnectedPlayerCount == Config.PlayerCount)
                        OnAllReady?.Invoke();
                    return i;
                }
            }
            return -1;
        }

        /// <summary>主机：peerId 断线 → 槽位释放并返回玩家 id（-1=该 peer 无槽位）。
        /// 断线重连窗口（ReconnectGraceMs）由 Task 11 ReconnectHandler 接入；本层断线即释放。</summary>
        public int HandleDisconnect(int peerId)
        {
            if (!IsHost)
                throw new InvalidOperationException("仅主机可处理断线");
            for (int i = 0; i < _slotOwnerPeer.Length; i++)
            {
                if (_slotOwnerPeer[i] == peerId)
                {
                    _slotOwnerPeer[i] = -1;
                    OnPlayerLeft?.Invoke(i);
                    return i;
                }
            }
            return -1;
        }

        /// <summary>客户端：应用 JoinAccept 结果（playerId、seed、角色表），会话进入 InGame。</summary>
        public void ApplyJoinAccept(int playerId, int seed, int[] defIds)
        {
            if (IsHost)
                throw new InvalidOperationException("仅客户端可应用加入结果");
            if (defIds == null || defIds.Length != Config.PlayerCount)
                throw new ArgumentException($"角色表长度 {defIds?.Length ?? 0} 必须等于 PlayerCount {Config.PlayerCount}", nameof(defIds));
            LocalPlayerId = playerId;
            Seed = seed;
            _playerDefIds = new int[defIds.Length];
            Array.Copy(defIds, _playerDefIds, defIds.Length);
            Phase = SessionPhase.InGame;
        }

        /// <summary>推进状态机（轮询断线超时等）；由驱动层每帧调用。
        /// 本层暂无周期逻辑：窗口超时在 NetworkGameLoop，断线超时/重连在 Task 11 ReconnectHandler。</summary>
        public void Poll() { }

        /// <summary>主机：已连接玩家数达到 PlayerCount 且处于 Lobby 时开战（Phase→InGame），返回 true。</summary>
        public bool TryStartGame()
        {
            if (!IsHost)
                throw new InvalidOperationException("仅主机可开战");
            if (ConnectedPlayerCount == Config.PlayerCount && Phase == SessionPhase.Lobby)
            {
                Phase = SessionPhase.InGame;
                return true;
            }
            return false;
        }
    }
}
