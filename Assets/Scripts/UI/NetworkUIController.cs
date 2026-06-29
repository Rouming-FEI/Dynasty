using System;
using System.Collections.Generic;
using Core;
using Core.Command;
using Core.Replay;
using Gameplay.Characters;
using Gameplay.Judgment;
using Network.Protocol;
using Network.Session;
using Network.Transport;
using UI.Presenter;
using UnityEngine;
using UnityEngine.UIElements;

namespace UI
{
    /// <summary>
    /// 网络对战宿主（正式对战 UI）：建房/入房 → 房间 → 对局（驱动 NetworkGameLoop.Tick）→
    /// 断线重连（客户端 = 全新会话容器，P6 语义）→ 终局。实现 INetworkBattleDriver 供 BattleUI
    /// 网络分支读状态/交输入。挂载要求：UIDocument（Source Asset = NetworkUI.uxml）+ Inspector
    /// 引用 BattleUIController 与 ChallengeUIController（入口互切）。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class NetworkUIController : MonoBehaviour, INetworkBattleDriver
    {
        [SerializeField] private BattleUIController _battleUI;
        [SerializeField] private ChallengeUIController _challengeUI;
        [SerializeField] private int _defaultPort = 7777;
        [SerializeField] private int _maxReconnectAttempts = 10;
        [SerializeField] private float _reconnectIntervalSec = 2f;

        // ---- 会话容器（客户端重连 = 整体重建） ----
        private NetworkTransport _transport;
        private NetworkSession _session;
        private SessionConfig _config;
        private NetworkGameLoop _loop;
        private bool _isHost;

        // ---- 窗口/断线状态 ----
        private int _judgmentMs;
        private float _windowDeadline;          // Time.time 语义倒计时终点
        private string _windowText = "";
        private bool _reconnecting;
        private int _reconnectAttempt;
        private float _nextReconnectAt;
        private bool _overlayOpen;              // 弹层当前是否占用（断线/终局互斥）
        private bool _roomDirty;                // 主机房间屏刷新标记（真人入座事件早于槽角色写入，延迟下一帧重渲染）

        // ---- UI 元素（Start 内赋值） ----
        private VisualElement _menuRoot, _roomRoot, _bannerRoot, _overlayRoot;
        private Label _menuError, _roomTitle, _roomSeed, _roomList, _roomHint;
        private Label _bannerStatus, _bannerWindow, _overlayTitle, _overlayBody, _overlayError;
        private Button _overlayPrimary, _overlaySecondary;
        private DropdownField _hostCountField, _hostRoleField, _clientRoleField;
        private TextField _hostPortField, _hostSeedField, _clientIpField, _clientPortField;
        private int[] _roleIds;                 // 角色下拉 id 表（GetAllIds 序）
        private Action _primaryAction, _secondaryAction;
        private bool _started;                  // Unity 依赖字段注入（挑战 UI 启动序可能早于本组件）

        // ---- INetworkBattleDriver ----
        public WorldState State => _loop != null ? _loop.State : null;
        public int LocalPlayerId => _session != null ? _session.LocalPlayerId : -1;
        public int CurrentActorPlayerId => _loop != null ? _loop.CurrentActorPlayerId : -1;

        public void SubmitChip(ChipAllocation alloc)
        {
            if (_loop == null) return;
            _loop.SubmitChip(alloc.PlayerId, alloc);
        }

        public void SubmitCommand(ICommand cmd)
        {
            if (_loop == null) return;
            ReplayActionRecord rec = ReplayActionFactory.ToRecord(cmd);
            if (rec.Kind == ReplayActionKind.Unknown)
            {
                Debug.LogError($"[NetUI] 命令无法转网络记录（Kind=Unknown）: {cmd.GetType().Name}");
                return;
            }
            _loop.SubmitAction(cmd.PlayerId, rec);
        }

        // ==================== MonoBehaviour ====================

        private void Start()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            if (root == null)
            {
                Debug.LogError("[NetUI] UIDocument.rootVisualElement 为空——请在 Inspector 为 UIDocument 拖入 NetworkUI.uxml");
                enabled = false;
                return;
            }
            _menuRoot = root.Q<VisualElement>("menu-root");
            _roomRoot = root.Q<VisualElement>("room-root");
            _bannerRoot = root.Q<VisualElement>("banner-root");
            _overlayRoot = root.Q<VisualElement>("overlay-root");
            _menuError = root.Q<Label>("menu-error-label");
            _roomTitle = root.Q<Label>("room-title-label");
            _roomSeed = root.Q<Label>("room-seed-label");
            _roomList = root.Q<Label>("room-list-label");
            _roomHint = root.Q<Label>("room-hint-label");
            _bannerStatus = root.Q<Label>("banner-status-label");
            _bannerWindow = root.Q<Label>("banner-window-label");
            _overlayTitle = root.Q<Label>("overlay-title-label");
            _overlayBody = root.Q<Label>("overlay-body-label");
            _overlayError = root.Q<Label>("overlay-error-label");
            _overlayPrimary = root.Q<Button>("overlay-primary-button");
            _overlaySecondary = root.Q<Button>("overlay-secondary-button");
            _hostPortField = root.Q<TextField>("host-port-field");
            _hostSeedField = root.Q<TextField>("host-seed-field");
            _clientIpField = root.Q<TextField>("client-ip-field");
            _clientPortField = root.Q<TextField>("client-port-field");
            _hostCountField = root.Q<DropdownField>("host-count-field");
            _hostRoleField = root.Q<DropdownField>("host-role-field");
            _clientRoleField = root.Q<DropdownField>("client-role-field");

            _roleIds = CharacterCatalog.GetAllIds();
            var names = new List<string>(_roleIds.Length);
            for (int i = 0; i < _roleIds.Length; i++)
                names.Add(CharacterCatalog.Create(_roleIds[i]).Name);
            var counts = new List<string>();
            for (int n = 2; n <= 8; n++) counts.Add($"{n} 人局");
            _hostCountField.choices = counts;
            _hostCountField.index = 2;      // 默认 4 人局
            _hostRoleField.choices = names;
            _hostRoleField.index = 0;
            _clientRoleField.choices = names;
            _clientRoleField.index = 0;

            root.Q<Button>("host-start-button").clicked += OnHostStart;
            root.Q<Button>("client-join-button").clicked += OnClientJoin;
            root.Q<Button>("back-single-button").clicked += OnBackToSingle;
            root.Q<Button>("room-start-button").clicked += OnRoomStart;
            root.Q<Button>("room-leave-button").clicked += OnLeaveRoom;
            root.Q<Button>("banner-quit-button").clicked += OnQuitMatch;
            _overlayPrimary.clicked += () => _primaryAction?.Invoke();
            _overlaySecondary.clicked += () => _secondaryAction?.Invoke();

            // 默认常驻置顶（盖住热座/挑战画面）；进入方式 = 挑战主菜单「网络对战」按钮
            _roomRoot.style.display = DisplayStyle.None;
            _bannerRoot.style.display = DisplayStyle.None;
            _overlayRoot.style.display = DisplayStyle.None;
            _menuRoot.style.display = DisplayStyle.None;
            _started = true;
        }

        private void Update()
        {
            if (!_started) return;
            if (_roomDirty)   // 主机：真人入座后的延迟房间屏刷新（会话事件早于槽角色写入，下一帧渲染最终表）
            {
                _roomDirty = false;
                if (_isHost && _loop != null && _loop.State == null) RefreshRoomList();
            }
            if (_loop != null)
            {
                _loop.Tick();   // PollEvents + 驱动（主机决策 / 客户端重放）
                RefreshBanner();
                TryFinishBattleEntry();      // 开局后首次非空 State → 接 BattleUI
                CheckInGameEnd();            // GameOver → 终局层
                PumpReconnect();             // 客户端断线自动重连状态机
            }
        }

        // ==================== 建房 / 入房 ====================

        /// <summary>挑战主菜单「网络对战」入口（ChallengeUIController 调用）：显示主菜单。</summary>
        public void ShowMainMenu()
        {
            _menuError.text = "";
            _menuRoot.style.display = DisplayStyle.Flex;
        }

        /// <summary>回单人模式（挑战菜单）：隐藏自身全部屏。由 ChallengeUIController.ShowMenu 承接。</summary>
        private void OnBackToSingle()
        {
            QuitAll();
            _challengeUI?.ShowMenu();
        }

        /// <summary>建房：读取表单 → 装配主机会话 → 监听端口 → 房间屏。</summary>
        private void OnHostStart()
        {
            if (_transport != null) QuitAll();
            int port = ParsePort(_hostPortField.value, _defaultPort);
            int count = 2 + _hostCountField.index;             // choices 序 = 2..8
            int roleId = _roleIds[_hostRoleField.index];
            int seed = ParseSeed(_hostSeedField.value);
            _isHost = true;
            _config = new SessionConfig
            {
                PlayerCount = count,
                RandomSeed = seed,
                HostPlayerDefIds = BuildInitialDefs(count, roleId)   // 槽 0 = roleId，其余 -1
            };
            _transport = new NetworkTransport();
            _session = new NetworkSession(_config, true);
            _loop = new NetworkGameLoop(_transport, _session, _config);
            SubscribeLoopEvents();

            try
            {
                _transport.StartHost(port, _config.MaxPeers, _config.ConnectKey);
            }
            catch (Exception e)
            {
                _menuError.text = $"启动监听失败：{e.Message}";
                QuitAll();
                ShowMainMenu();
                return;
            }
            _menuRoot.style.display = DisplayStyle.None;
            EnterRoomScreen();
        }

        /// <summary>加入：读取表单 → 装配客户端会话 → Connect → 等待 OnPeerConnected 发 Hello。
        /// 房间规模未知：SessionConfig.PlayerCount 用占位值（MaxPeers=8），JoinAccept 到达后
        /// loop 内按表长度修正（Task 3 JoinAccept case 语义——房间规模权威 = 主机表）。</summary>
        private void OnClientJoin()
        {
            if (_transport != null) QuitAll();
            string ip = _clientIpField.value;
            int port = ParsePort(_clientPortField.value, _defaultPort);
            int roleId = _roleIds[_clientRoleField.index];
            _isHost = false;
            _config = new SessionConfig { PlayerCount = 8 };   // 占位；JoinAccept 后按表修正
            _transport = new NetworkTransport();
            _session = new NetworkSession(_config, false);
            _loop = new NetworkGameLoop(_transport, _session, _config);
            _loop.RequestedDefId = roleId;
            SubscribeLoopEvents();

            _menuError.text = "";
            _menuRoot.style.display = DisplayStyle.None;
            _roomTitle.text = "连接中…";
            _roomSeed.text = "";
            _roomList.text = $"正在连接 {ip}:{port}…";
            _roomHint.text = "";
            _roomRoot.style.display = DisplayStyle.Flex;
            SetRoomButtons(false, "离开房间");
            SetOverlayVisible(false, null, null, null, null, null);

            _transport.OnPeerConnected += peerId =>
            {
                if (!_isHost && _session.Phase == SessionPhase.Lobby && _loop != null)
                    _loop.SendHello();   // 新加入（重连路径不走到这：断线后旧 loop 已弃用）
            };
            try
            {
                if (!_transport.Connect(ip, port, _config.ConnectKey, 3000))
                {
                    _roomList.text = $"连接失败（无法发起）：请确认主机已开房且 IP/端口正确";
                    SetRoomButtons(false, "返回主菜单");
                    return;
                }
            }
            catch (Exception e)
            {
                _roomList.text = $"连接异常：{e.Message}";
                SetRoomButtons(false, "返回主菜单");
            }
        }

        /// <summary>主机开战：TryStartGame + loop.StartGame（AI 空槽补位在 loop 内）。</summary>
        private void OnRoomStart()
        {
            if (!_isHost || _loop == null) return;
            if (!_session.TryStartGame()) return;
            _loop.StartGame();
        }

        private void EnterRoomScreen()
        {
            _roomRoot.style.display = DisplayStyle.Flex;
            _roomTitle.text = $"房间已创建（{_config.PlayerCount} 人局）· 主机";
            _roomSeed.text = $"种子 {_config.RandomSeed} · 角色 {RoleName(_config.HostPlayerDefIds[0])}";
            _roomHint.text = "等待玩家加入…（开战后空位将由 AI 补齐）";
            SetRoomButtons(true, "开战");
            RefreshRoomList();
        }

        private void SetRoomButtons(bool canStart, string leaveText)
        {
            var start = _roomRoot.Q<Button>("room-start-button");
            var leave = _roomRoot.Q<Button>("room-leave-button");
            if (start != null) { start.style.display = canStart && _isHost ? DisplayStyle.Flex : DisplayStyle.None; }
            if (leave != null) leave.text = leaveText;
        }

        private void OnLeaveRoom()
        {
            QuitAll();
            ShowMainMenu();
        }

        // ==================== 会话事件接线 ====================

        private void SubscribeLoopEvents()
        {
            // 主机房间屏真人入座刷新（补计划缺口）：loop.OnJoined 是客户端专属入座回执
            // （JoinAccept case，`if (!IsHost)` 内触发），主机不触发——若只挂 OnJoined，
            // 主机房间列表在玩家加入后永不刷新。session.OnPlayerJoined 在主机 HandleHello
            // 入座时同步触发，但早于槽角色写入（SetPlayerDef 在其后），故只置脏标记，
            // 由 Update 下一帧统一重渲染最终表（含入座角色）。
            _session.OnPlayerJoined += pid =>
            {
                if (_isHost && _loop != null && _loop.State == null) _roomDirty = true;
            };
            _loop.OnJoined += pid =>
            {
                if (_isHost)
                {
                    RefreshRoomList();   // 主机：新玩家入座刷新房间屏（本端实际不触发，客户端回执专用）
                }
                else   // 客户端入座成功（新加入 / 房间期断线重连的重新入座）→ 房间等待态
                {
                    if (_reconnecting && !_inBattle)
                    {
                        StopReconnect(true);   // 房间期重连成功（覆盖层关闭，回到房间屏）
                        _roomRoot.style.display = DisplayStyle.Flex;
                    }
                    _roomTitle.text = "已加入房间";
                    _roomList.text = $"你的角色：{RoleName(_session.PlayerDefIds[pid])}\n等待房主开战…";
                    _roomSeed.text = "";
                    SetRoomButtons(false, "离开房间");
                }
            };
            _loop.OnJoinRejected += reason =>
            {
                string why = reason == NetProtocol.JoinRejectReasonRoleTaken ? "角色已被其他玩家占用"
                    : reason == NetProtocol.JoinRejectReasonVersion ? "版本不符（请同步更新）"
                    : "房间满员或已开局";
                QuitAll();
                ShowMainMenu();
                _menuError.text = $"加入被拒绝：{why}";
            };
            _loop.OnPeerLeft += pid =>   // 主机视角
            {
                if (_isHost) RefreshRoomList();
            };
            _loop.OnDisconnected += () =>
            {
                if (!_isHost && !_reconnecting) StartAutoReconnect();
            };
            _loop.OnJudgmentWindow += (turn, ms) =>
            {
                _judgmentMs = ms;
                _windowDeadline = Time.time + ms / 1000f;
                _windowText = "判定窗口开启";
                _battleUI?.NotifyJudgmentWindow();
            };
            _loop.OnActionWindow += (pid, ms) =>
            {
                _judgmentMs = ms;
                _windowDeadline = Time.time + ms / 1000f;
                _windowText = pid == LocalPlayerId ? "轮到你行动" : $"等待 {RoleNameOfPlayer(pid)} 行动";
                if (pid == LocalPlayerId) _battleUI?.NotifyMyActionWindow();
            };
            _loop.OnError += msg =>
            {
                Debug.LogError($"[NetUI] 会话错误: {msg}");
                _bannerStatus.text = $"错误：{msg}";
            };
        }

        private string RoleName(int defId)
        {
            if (defId <= 0) return "（AI 空位）";
            var def = CharacterDefCatalog.Get(defId);
            return def != null ? def.Name : $"角色{defId}";
        }

        private string RoleNameOfPlayer(int playerId)
        {
            if (_session == null || _session.PlayerDefIds == null
                || playerId < 0 || playerId >= _session.PlayerDefIds.Length) return "?";
            return RoleName(_session.PlayerDefIds[playerId]);
        }

        /// <summary>房间屏玩家列表（主机重渲染；客户端加入态由 OnJoined 文本承担）。</summary>
        private void RefreshRoomList()
        {
            if (_session == null || !_isHost) return;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("—— 玩家列表 ——");
            for (int i = 0; i < _session.Config.PlayerCount; i++)
            {
                bool human = i == 0 || _session.PlayerPeerIds[i] != -1;
                string who = human
                    ? (_session.PlayerPeerIds[i] == -1 ? "（主机）" : "（玩家）")
                    : "（AI 空位）";
                sb.AppendLine($"槽{i}：{RoleName(_session.PlayerDefIds[i])} {who}");
            }
            _roomList.text = sb.ToString();
            if (_session.ConnectedPlayerCount == _session.Config.PlayerCount - 1)
                _roomHint.text = "玩家已满，可开战！";
        }

        // ==================== 对局中 ====================

        /// <summary>开局后（State 非空且未接 BattleUI）首次接入战斗屏。</summary>
        private void TryFinishBattleEntry()
        {
            if (_battleUI == null) return;
            if (_inBattle) return;
            if (_loop == null || _loop.State == null) return;
            _inBattle = true;
            _roomRoot.style.display = DisplayStyle.None;
            _bannerRoot.style.display = DisplayStyle.Flex;
            _battleUI.BeginNetworkedBattle(this);
            RefreshBanner();
        }
        private bool _inBattle;

        private void CheckInGameEnd()
        {
            if (!_inBattle) return;
            if (_loop == null || _loop.State == null) return;
            if (_loop.State.Phase != TurnPhase.GameOver) return;
            EndNetworkedMatch(false);
        }

        private void RefreshBanner()
        {
            if (_bannerRoot == null || _bannerRoot.style.display == DisplayStyle.None) return;
            if (_loop == null || _loop.State == null) return;
            var st = _loop.State;
            string phase = st.Phase == TurnPhase.GameOver ? "已结束"
                : st.Phase == TurnPhase.JudgmentPhase ? $"第{st.CurrentTurn}回合·判定"
                : st.Phase == TurnPhase.ActionPhase ? $"第{st.CurrentTurn}回合·行动" : "结算";
            string conn = _reconnecting ? "断线重连中…" : (_isHost ? "主机" : "已连接");
            _bannerStatus.text = $"{phase} · 存活 {st.AliveCharacterCount}/{st.PlayerCount} · {conn}";

            if (_judgmentMs > 0 && !_reconnecting)
            {
                float left = _windowDeadline - Time.time;
                _bannerWindow.text = left > 0
                    ? $"{_windowText}（{(int)Math.Ceiling(left)}s）"
                    : _windowText;
            }
            else if (_reconnecting)
            {
                _bannerWindow.text = "等待重连…";
            }
        }

        private void SetOverlayVisible(bool open, string title, string body,
            string primaryText, Action primary, string secondaryText = null, Action secondary = null)
        {
            if (_overlayRoot == null) return;
            _overlayRoot.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            if (!open) return;
            _overlayTitle.text = title ?? "";
            _overlayBody.text = body ?? "";
            _overlayError.text = "";
            _overlayPrimary.text = primaryText ?? "";
            _primaryAction = primary;
            _overlaySecondary.style.display = secondary == null ? DisplayStyle.None : DisplayStyle.Flex;
            _overlaySecondary.text = secondaryText ?? "";
            _secondaryAction = secondary;
        }

        // ==================== 断线 / 重连（客户端） ====================

        private void StartAutoReconnect()
        {
            if (_isHost) return;   // 主机掉线 = 房间解散（P6 决策：客户端重连数次失败后提示）
            _reconnecting = true;
            _reconnectAttempt = 0;
            _nextReconnectAt = Time.time;
            _overlayOpen = true;
            SetOverlayVisible(true, "连接断开",
                "正在尝试自动重连…", "放弃", () => { StopReconnect(false); ShowMainMenu(); });
        }

        /// <summary>
        /// 自动重连泵：每 _reconnectIntervalSec 尝试一次全新会话容器（P6 语义：新 transport +
        /// 新 session + 新 loop）→ Connect 成功 → SendResyncRequest 全量重放追平。
        /// </summary>
        private void PumpReconnect()
        {
            if (!_reconnecting) return;
            if (_inBattle)
            {
                // 重连成功判定：新 loop 追平完成（IsResyncing=false 且 State 非空）
                if (!_loop.Reconnect.IsResyncing && _loop.State != null)
                {
                    StopReconnect(true);
                    // 重连 = 全新 WorldState：卸载旧 presenter（绑旧世界）并重建绑定新世界
                    _battleUI?.EndNetworkedBattle();
                    _battleUI?.BeginNetworkedBattle(this);
                    return;
                }
                // 追平期间继续驱动 Tick（客户端消费重放批次）
                _bannerWindow.text = $"重连中（第 {_reconnectAttempt} 次）…";
                return;
            }
            if (Time.time < _nextReconnectAt) return;
            if (_reconnectAttempt >= _maxReconnectAttempts)
            {
                SetOverlayVisible(true, "重连失败",
                    "无法重新连接主机。\n主机可能已关闭（房间解散）。", "返回主菜单",
                    () => { StopReconnect(false); ShowMainMenu(); });
                return;
            }
            _reconnectAttempt++;
            _nextReconnectAt = Time.time + _reconnectIntervalSec;

            // 重连语义分叉：对局期（旧 loop 已开局）→ 全量追平 Resync；
            // 房间期（旧 loop 未开局，主机还在 Lobby）→ 以原角色重新 Hello 入座。
            bool wasInGame = _loop != null && _loop.State != null;
            int playerId = _session.LocalPlayerId;
            int seed = _session.Seed;
            int[] defIds = _session.PlayerDefIds;
            int myDefId = defIds != null && playerId >= 0 && playerId < defIds.Length
                ? defIds[playerId] : -1;   // 房间期重连的原角色（入座后表已含）
            string host = _clientIpField.value;
            int port = ParsePort(_clientPortField.value, _defaultPort);
            QuitTransportOnly();
            _config = new SessionConfig { PlayerCount = defIds != null ? defIds.Length : 8 };
            _transport = new NetworkTransport();
            _session = new NetworkSession(_config, false);
            if (wasInGame)
            {
                if (defIds == null || defIds.Length == 0)
                {
                    // 未入座即断线（理论不可达：对局期必已入座）→ 放弃自动重连
                    SetOverlayVisible(true, "重连失败", "会话状态缺失，无法重连。", "返回主菜单",
                        () => { StopReconnect(false); ShowMainMenu(); });
                    return;
                }
                _session.ApplyJoinAccept(playerId, seed, defIds);   // 重建入座态 → Resync 路径
            }
            else
            {
                _loop.RequestedDefId = myDefId > 0 ? myDefId : _roleIds.Length > 0 ? _roleIds[0] : 1;
                // 保持 Phase=Lobby：重连成功后走 Hello 重新入座（主机房间期接受新 Hello）
            }
            _loop = new NetworkGameLoop(_transport, _session, _config);
            SubscribeLoopEvents();
            var t = _transport;
            t.OnPeerConnected += id =>
            {
                if (_isHost || _loop == null) return;
                if (wasInGame)
                    _loop.SendResyncRequest();   // 对局期：锚点缺省 (0,0) 全量重放追平
                else
                    _loop.SendHello();           // 房间期：原角色重新入座
            };
            try
            {
                if (t.Connect(host, port, _config.ConnectKey, 3000))
                    _bannerWindow.text = "已连接，同步中…";
                else
                    _bannerWindow.text = $"第 {_reconnectAttempt} 次重连失败…";
            }
            catch (Exception)
            {
                _bannerWindow.text = $"第 {_reconnectAttempt} 次重连异常…";
            }
        }

        private void StopReconnect(bool success)
        {
            _reconnecting = false;
            if (success)
            {
                _bannerStatus.text = "已重连";
                _bannerWindow.text = "";
                SetOverlayVisible(false, null, null, null, null);
            }
            else
            {
                QuitAll();
            }
        }

        // ==================== 终局 / 清理 ====================

        private void EndNetworkedMatch(bool hostQuit)
        {
            _inBattle = false;
            _battleUI?.EndNetworkedBattle();
            _bannerRoot.style.display = DisplayStyle.None;
            // 胜者文本（与热座结算同源：存活者名单）
            string winner = BuildWinnerText(_loop != null ? _loop.State : null);
            SetOverlayVisible(true, hostQuit ? "你已退出对局" : "对局结束",
                winner, "回主菜单", () => { QuitAll(); ShowMainMenu(); },
                hostQuit ? null : null, null);
        }

        private static string BuildWinnerText(WorldState state)
        {
            if (state == null) return "对局中断。";
            var sb = new System.Text.StringBuilder();
            foreach (Character c in state.Characters)
            {
                if (c == null || c.IsDead) continue;
                if (sb.Length > 0) sb.Append("、");
                sb.Append(c.Name);
            }
            return sb.Length == 0 ? "无人存活" : $"胜者：{sb}\n\n感谢游玩，回到主菜单可再来一局。";
        }

        private void OnQuitMatch()
        {
            if (!_inBattle) return;
            EndNetworkedMatch(true);
        }

        /// <summary>断开连接并释放容器（保留 UI 静态元素）。</summary>
        private void QuitTransportOnly()
        {
            if (_transport != null)
            {
                try { _transport.Stop(); } catch (Exception) { }
                _transport = null;
            }
        }

        private void QuitAll()
        {
            QuitTransportOnly();
            _session = null;
            _loop = null;
            _isHost = false;
            _reconnecting = false;
            _reconnectAttempt = 0;
            _inBattle = false;
            _judgmentMs = 0;
            _roomDirty = false;
            _bannerRoot.style.display = DisplayStyle.None;
            _roomRoot.style.display = DisplayStyle.None;
            _overlayRoot.style.display = DisplayStyle.None;
            _battleUI?.EndNetworkedBattle();
        }

        // ==================== 工具 ====================

        private static int[] BuildInitialDefs(int count, int hostRoleId)
        {
            var defs = new int[count];
            defs[0] = hostRoleId;
            for (int i = 1; i < count; i++) defs[i] = -1;   // AI 空位（开战补位）
            return defs;
        }

        private static int ParsePort(string s, int fallback)
        {
            return int.TryParse(s, out int v) && v > 0 && v < 65536 ? v : fallback;
        }

        private static int ParseSeed(string s)
        {
            if (!string.IsNullOrEmpty(s) && int.TryParse(s, out int v)) return v;
            int rnd = Environment.TickCount ^ Guid.NewGuid().GetHashCode();
            return rnd == 0 ? 42 : rnd;
        }
    }
}
