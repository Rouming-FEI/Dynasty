using System;

namespace Network.Session
{
    /// <summary>会话阶段：Lobby（玩家加入/分配槽位）→ InGame → GameOver。</summary>
    public enum SessionPhase { Lobby, InGame, GameOver }

    /// <summary>
    /// 会话状态机：Lobby（玩家加入/分配槽位）→ InGame → GameOver。
    /// 槽位模型（spec 3.3）：槽 0 恒为主机本地玩家（LocalPlayerId=0，不经过 HandleHello），
    /// 对端从槽 1 起分配（playerId 1..PlayerCount-1），满员 = 对端槽全部被占。
    /// 主机侧持有 playerId（槽号）→peerId 映射，以 playerId 为下标、槽 0 恒 -1。
    /// 断线重连窗口（ReconnectGraceMs）由 Task 11 ReconnectHandler 接入；本层断线即释放槽位。
    /// 纯 C# 状态机，不依赖真实 UDP。</summary>
    public sealed class NetworkSession
    {
        private readonly int[] _slotOwnerPeer;   // playerId（槽号）→peerId（槽 0 恒 -1=主机本地，1..N-1=对端）
        private int[] _playerDefIds;

        public SessionPhase Phase { get; private set; }
        public bool IsHost { get; }
        public int LocalPlayerId { get; private set; }   // 主机 = 0
        public int Seed { get; private set; }
        public int[] PlayerDefIds => _playerDefIds;      // 槽位角色表（长度=PlayerCount）
        public SessionConfig Config { get; }

        /// <summary>playerId（槽号）→peerId（-1=AI 槽/未连接），槽 0 恒 -1；调用方只读勿改。</summary>
        public int[] PlayerPeerIds => _slotOwnerPeer;

        /// <summary>已连接对端数（槽 1..PlayerCount-1 的占用数，不含主机），供上层显示/逻辑用。</summary>
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

        /// <summary>满员信号：对端槽（1..PlayerCount-1）全部被占时触发。
        /// 触发后驱动（NetworkGameLoop）须调用 TryStartGame 完成开战。</summary>
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

        /// <summary>主机：处理 Hello（peerId, requestedDefId）→ 从槽 1 起分配第一个空槽。
        /// 槽 0 恒为主机本地玩家、不参与分配；满员 = 对端槽（1..PlayerCount-1）全部被占。
        /// 返回分配的 playerId（=槽号），-1=拒绝（满员或已开战）。角色协商由上层负责，本层按主机配置表分配。
        /// 防御：同一 peerId 重复 Hello 返回其已持有的 playerId，不重复占槽。</summary>
        public int HandleHello(int peerId, int requestedDefId)
        {
            if (!IsHost)
                throw new InvalidOperationException("仅主机可处理加入请求");
            if (Phase != SessionPhase.Lobby)
                return -1;
            // I2：重复 peerId 防御——已持槽则返回原 playerId（不新占槽、不重复触发加入事件）
            for (int i = 1; i < _slotOwnerPeer.Length; i++)
            {
                if (_slotOwnerPeer[i] == peerId)
                    return i;
            }
            for (int i = 1; i < _slotOwnerPeer.Length; i++)
            {
                if (_slotOwnerPeer[i] == -1)
                {
                    _slotOwnerPeer[i] = peerId;
                    OnPlayerJoined?.Invoke(i);
                    if (ConnectedPlayerCount == Config.PlayerCount - 1)
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
            if (playerId < 0 || playerId >= Config.PlayerCount)
                throw new ArgumentException($"playerId {playerId} 越界（须在 [0, {Config.PlayerCount - 1}]）", nameof(playerId));
            if (defIds == null || defIds.Length != Config.PlayerCount)
                throw new ArgumentException($"角色表长度 {defIds?.Length ?? 0} 必须等于 PlayerCount {Config.PlayerCount}", nameof(defIds));
            LocalPlayerId = playerId;
            Seed = seed;
            _playerDefIds = new int[defIds.Length];
            Array.Copy(defIds, _playerDefIds, defIds.Length);
            Phase = SessionPhase.InGame;
        }

        /// <summary>主机：重连者拿回原槽位（peerId 重新持有 playerId 槽，经 OnPlayerJoined 解冻信号）。
        /// 校验：InGame 阶段；playerId ∈ [1, PlayerCount-1]（槽 0 恒主机本地）；槽位空闲或已属于该 peer。
        /// 返回 true = 重连成功（槽位已重新分配）。</summary>
        public bool RejoinSlot(int peerId, int playerId)
        {
            if (!IsHost)
                throw new InvalidOperationException("仅主机可处理重连");
            if (Phase != SessionPhase.InGame)
                return false;
            if (playerId <= 0 || playerId >= _slotOwnerPeer.Length)
                return false;
            if (_slotOwnerPeer[playerId] != -1 && _slotOwnerPeer[playerId] != peerId)
                return false;   // 槽位被其他对端持有
            _slotOwnerPeer[playerId] = peerId;
            OnPlayerJoined?.Invoke(playerId);
            return true;
        }

        /// <summary>推进状态机（轮询断线超时等）；由驱动层每帧调用。
        /// 本层暂无周期逻辑：窗口超时在 NetworkGameLoop，断线超时/重连在 Task 11 ReconnectHandler。</summary>
        public void Poll() { }

        /// <summary>主机：手动开战入口——Lobby 阶段调用即切 InGame 并返回 true（无需满员，AI 槽由
        /// NetworkGameLoop 本地填，真人 1..PlayerCount-1 人即可开战）。满员自动开战由 OnAllReady
        /// 触发（NetworkGameLoop 接线：收到信号后调本方法完成开战）；已开战（InGame/GameOver）返回 false。</summary>
        public bool TryStartGame()
        {
            if (!IsHost)
                throw new InvalidOperationException("仅主机可开战");
            if (Phase == SessionPhase.Lobby)
            {
                Phase = SessionPhase.InGame;
                return true;
            }
            return false;
        }
    }
}
