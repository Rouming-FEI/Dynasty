using System;
using System.Collections.Generic;
using System.Text;
using Core;
using Core.Command;
using Core.GAS;
using Gameplay.Characters;
using Gameplay.Items;
using Gameplay.Judgment;
using Gameplay.Movement;
using Network.Local;
using UI.ViewModels;

namespace UI.Presenter
{
    /// <summary>
    /// UI 与模拟的单一通道（纯 C#，无 UnityEngine 引用）。
    /// 读 WorldState 构建快照；Bind 后消费输入意图（筹码/指令，恰好一次）。
    /// 生命周期约束：一个 WorldState 仅对应一个 BattlePresenter（EventBus 无解绑 API，
    /// 重复挂载会日志重复入队且闭包被 Events 强持有）；换局请连同 WorldState 一起新建。
    /// </summary>
    public class BattlePresenter
    {
        private const int LogCapacity = 200;
        private const int LogTailCount = 8;

        private readonly WorldState _state;
        private readonly Queue<string> _log = new Queue<string>(LogCapacity);
        private readonly string[] _handNames = { "石头", "剪刀", "布" };
        private LocalLockstep _lockstep;   // Bind 后赋值；未绑定时 CurrentActorPlayerId 返回 -1

        internal WorldState State => _state;

        public BattlePresenter(WorldState state)
        {
            _state = state;
            SubscribeLogEvents();
        }

        // ---------------- 快照 ----------------

        public BattleSnapshot BuildSnapshot()
        {
            var snap = new BattleSnapshot
            {
                CurrentTurn = _state.CurrentTurn,
                AliveCount = _state.AliveCharacterCount,
                PlayerCount = _state.PlayerCount,
                GameOver = _state.Phase == TurnPhase.GameOver
            };
            snap.PhaseText = snap.GameOver ? "游戏结束"
                : _state.Phase == TurnPhase.JudgmentPhase ? "判定阶段"
                : _state.Phase == TurnPhase.ActionPhase ? "行动阶段" : "回合结算";
            if (snap.GameOver)
                snap.WinnerText = BuildWinnerText();

            int actorPlayerId = CurrentActorPlayerId();
            var nodePlayers = new Dictionary<int, List<string>>();
            int turretLocation = -1;

            foreach (Character c in _state.Characters)
            {
                if (c == null) continue;
                bool hidden = c.ASC.Tags.HasTag(GameplayTagConstants.HideoutHidden)
                           || c.ASC.Tags.HasTag(GameplayTagConstants.MistState);
                var row = new BattleSnapshot.CharacterRow
                {
                    PlayerId = c.PlayerId,
                    Name = c.Name,
                    Sanity = c.Sanity,
                    MaxSanity = c.MaxSanity,
                    ActionPoints = c.ActionPoints,
                    LocationName = hidden ? "??" : _state.Locations.GetName(c.CurrentLocationId),
                    IsDead = c.IsDead,
                    IsHidden = hidden,
                    IsShielded = c.ASC.Tags.HasTag(GameplayTagConstants.HasShield),
                    IsCurrentActor = c.PlayerId == actorPlayerId,
                    WeaponName = c.Inventory.EquippedWeapon?.Name,
                    WeaponAmmo = c.Inventory.EquippedWeaponAmmo
                };
                if (row.IsShielded) row.Tags.Add("盾");
                if (c.ASC.Tags.HasTag(GameplayTagConstants.Bound)) row.Tags.Add("束缚");
                if (c.ASC.Tags.HasTag(GameplayTagConstants.BoundStrong)) row.Tags.Add("强束缚");
                if (c.ASC.Tags.HasTag(GameplayTagConstants.LifeFire)) row.Tags.Add("生命之火");
                if (c.ASC.Tags.HasTag(GameplayTagConstants.OnTurret)) row.Tags.Add("炮台");
                if (c.ASC.Tags.HasTag(GameplayTagConstants.TurretLoaded)) row.Tags.Add("已装填");
                snap.CharacterRows.Add(row);

                if (c.IsDead) continue;
                if (c.ASC.Tags.HasTag(GameplayTagConstants.OnTurret))
                    turretLocation = c.CurrentLocationId;
                if (hidden) continue;
                if (!nodePlayers.TryGetValue(c.CurrentLocationId, out var names))
                {
                    names = new List<string>();
                    nodePlayers[c.CurrentLocationId] = names;
                }
                names.Add(c.Name);
            }

            Character actor = actorPlayerId >= 0
                ? _state.GetCharacterByPlayer(actorPlayerId) : null;

            for (int loc = 0; loc < _state.Locations.LocationCount; loc++)
            {
                var node = new BattleSnapshot.MapNodeInfo
                {
                    LocationId = loc,
                    Name = _state.Locations.GetName(loc),
                    HasTrap = _state.HasTrap(loc),
                    HasTurretOccupant = loc == turretLocation,
                    IsCurrentActorHere = actor != null && actor.CurrentLocationId == loc
                };
                if (nodePlayers.TryGetValue(loc, out var names))
                    node.PlayerNames.AddRange(names);
                foreach (var li in _state.Locations.GetLocatedItems(loc))
                    node.ItemBadges.Add($"{ItemRegistry.Get(li.ItemId)?.Name ?? "?"}×{li.Count}");
                snap.MapNodes.Add(node);
            }

            // 最近一次判定是否为全局平局：筹码面板据此显示「全局平局，重新分配」。
            // 平局作废后 TurnManager 保留作废结果（HasJudgmentResult/LastJudgment 不清）
            // 且 Phase 停留判定阶段——该窗口内此标记即「正在重判」信号。
            snap.LastJudgmentGlobalFlat = _state.HasJudgmentResult && _state.LastJudgment.IsGlobalFlat;

            if (_state.HasJudgmentResult && _state.Phase == TurnPhase.ActionPhase)
                snap.JudgmentSummary = BuildJudgmentSummary();

            string[] allLog = _log.ToArray();
            for (int i = System.Math.Max(0, allLog.Length - LogTailCount);
                 i < allLog.Length; i++)
                snap.LogTail.Add(allLog[i]);
            return snap;
        }

        /// <summary>当前行动者（未绑定且无 provider 时 -1）。网络模式读注入 provider。</summary>
        private int CurrentActorPlayerId()
            => _chipSink != null
                ? (_networkActorProvider != null ? _networkActorProvider() : -1)
                : (_lockstep != null ? _lockstep.CurrentActorPlayerId : -1);

        private string BuildWinnerText()
        {
            var sb = new StringBuilder();
            foreach (Character c in _state.Characters)
            {
                if (c == null || c.IsDead) continue;
                if (sb.Length > 0) sb.Append("、");
                sb.Append(c.Name);
            }
            return sb.Length == 0 ? "无人存活" : $"胜者：{sb}";
        }

        private string BuildJudgmentSummary()
        {
            JudgmentResult j = _state.LastJudgment;
            var sb = new StringBuilder();
            sb.Append($"第{_state.CurrentTurn}回合判定 胜手:{HandName(j.WinnerHand)}");
            if (j.LoserHand >= 0) sb.Append($" 败手:{HandName(j.LoserHand)}");
            for (int i = 0; i < _state.PlayerCount; i++)
            {
                Character c = _state.Characters[i];
                if (c == null || c.IsDead) continue;
                string role = j.Roles[i] == JudgmentRole.Winner ? "胜"
                            : j.Roles[i] == JudgmentRole.Loser ? "败" : "中";
                sb.Append($"｜{c.Name}{role}+{j.APGained[i]}AP");
            }
            return sb.ToString();
        }

        private string HandName(int h)
            => h >= 0 && h < 3 ? _handNames[h] : "-";

        // ---------------- 日志 ----------------

        /// <summary>完整战斗日志（容量截断后的当前队列内容，供日志面板全量展示）。</summary>
        public string[] GetFullLog() => _log.ToArray();

        private void EnqueueLog(string message)
        {
            if (_log.Count >= LogCapacity) _log.Dequeue();
            _log.Enqueue(message);
        }

        private void SubscribeLogEvents()
        {
            var events = _state.Events;
            events.Subscribe(GameplayEventTypes.EntityMoved, 0, (ref GameplayEvent e) =>
            {
                var c = _state.GetCharacterByPlayer(e.SourceId);
                EnqueueLog($"{c?.Name ?? "?"} 移动到 {_state.Locations.GetName(e.IntArg2)}");
            });
            events.Subscribe(GameplayEventTypes.PlayerDied, 0, (ref GameplayEvent e) =>
            {
                var c = _state.GetCharacter(e.TargetId);
                var killer = _state.GetCharacterByPlayer(e.IntArg1);
                // DamagePipeline 环境伤害（陷阱/潮汐/荆棘素）KillerPlayerId = -1 → 显示「环境」
                string killerName = killer?.Name ?? (e.IntArg1 < 0 ? "环境" : "?");
                EnqueueLog($"{c?.Name ?? "?"} 被 {killerName} 击杀");
            });
            events.Subscribe(GameplayEventTypes.DamageDealt, 0, (ref GameplayEvent e) =>
            {
                var src = _state.GetCharacter(e.SourceId);
                var tgt = _state.GetCharacter(e.TargetId);
                string srcName = src?.Name ?? (e.SourceId < 0 ? "环境" : "?");
                EnqueueLog($"{srcName} → {tgt?.Name ?? "?"} : {e.Magnitude:F1} 伤害");
            });
            events.Subscribe(GameplayEventTypes.OnShieldGranted, 0, (ref GameplayEvent e) =>
            {
                var c = _state.GetCharacter(e.TargetId);
                EnqueueLog($"{c?.Name ?? "?"} 获得护盾");
            });
            events.Subscribe(GameplayEventTypes.OnShieldBroken, 0, (ref GameplayEvent e) =>
            {
                var c = _state.GetCharacter(e.TargetId);
                EnqueueLog($"{c?.Name ?? "?"} 的护盾破碎");
            });
            events.Subscribe(GameplayEventTypes.NukeLaunched, 0, (ref GameplayEvent e) =>
            {
                var launcher = _state.GetCharacter(e.SourceId);
                EnqueueLog(e.IntArg1 == 1
                    ? $"{launcher?.Name ?? "?"} 的核弹发射成功！全场真实伤害"
                    : $"{launcher?.Name ?? "?"} 核弹发射失败");
            });
            events.Subscribe(GameplayEventTypes.TrapTriggered, 0, (ref GameplayEvent e) =>
            {
                EnqueueLog($"{_state.Locations.GetName(e.IntArg1)} 的触发式炸弹被引爆");
            });
            events.Subscribe(GameplayEventTypes.TurretFired, 0, (ref GameplayEvent e) =>
            {
                var src = _state.GetCharacter(e.SourceId);
                var tgt = _state.GetCharacter(e.TargetId);
                EnqueueLog($"{src?.Name ?? "?"} 操纵炮台轰击 {tgt?.Name ?? "?"}");
            });
            events.Subscribe(GameplayEventTypes.TidePulse, 0, (ref GameplayEvent e) =>
            {
                EnqueueLog("实验室深度潮汐判定完成");
            });
        }

        // ---------------- 输入意图（热座 pending + 网络 sink 分流） ----------------

        private int _humanPlayerId = -1;
        private ChipAllocation? _pendingJudgment;
        private ICommand _pendingCommand;

        // 网络模式（Task 6）：不 Bind——lockstep 输入钩子归 NetworkGameLoop（客户端纯重放安全）；
        // 输入经 sink 直通 loop.SubmitChip/SubmitAction；actor 查询走注入 provider
        private Func<int> _networkActorProvider;
        private Action<ChipAllocation> _chipSink;
        private Action<ICommand> _cmdSink;
        private bool _judgmentSubmitted;

        /// <summary>真人玩家 id（Bind/AttachNetwork 后有效；-1 = 未绑定）。</summary>
        public int HumanPlayerId => _humanPlayerId;

        /// <summary>是否有待消费的筹码提交（UI 判定面板显隐依据）。</summary>
        public bool HasPendingJudgment => _pendingJudgment != null;

        /// <summary>网络模式激活（sink 已装配；NetworkMode=false = 热座 Bind 语义）。</summary>
        public bool NetworkMode => _chipSink != null;

        /// <summary>网络模式：本端已提交判定（窗口事件 ResetJudgmentSubmitted 复位；筹码面板显隐用）。</summary>
        public bool JudgmentSubmitted => _judgmentSubmitted;

        /// <summary>
        /// 装配网络模式（替代 Bind）：playerId = 本端槽位；actorProvider = 当前行动者查询
        /// （NetworkGameLoop.CurrentActorPlayerId）；chipSink/cmdSink = 提交出口
        /// （驱动层转 loop.SubmitChip / SubmitAction）。不触碰 lockstep 钩子。
        /// </summary>
        public void AttachNetwork(int playerId, Func<int> actorProvider,
            Action<ChipAllocation> chipSink, Action<ICommand> cmdSink)
        {
            if (actorProvider == null) throw new ArgumentNullException(nameof(actorProvider));
            if (chipSink == null) throw new ArgumentNullException(nameof(chipSink));
            if (cmdSink == null) throw new ArgumentNullException(nameof(cmdSink));
            _lockstep = null;
            _humanPlayerId = playerId;
            _networkActorProvider = actorProvider;
            _chipSink = chipSink;
            _cmdSink = cmdSink;
            _pendingJudgment = null;
            _pendingCommand = null;
            _judgmentSubmitted = false;
        }

        /// <summary>复位已提交标记（每个判定窗口事件 = 新一轮提交允许）。</summary>
        public void ResetJudgmentSubmitted() => _judgmentSubmitted = false;

        /// <summary>卸载网络模式：回到未绑定状态（热座 Bind 前语义）。</summary>
        public void DetachNetwork()
        {
            _lockstep = null;
            _humanPlayerId = -1;
            _networkActorProvider = null;
            _chipSink = null;
            _cmdSink = null;
            _pendingJudgment = null;
            _pendingCommand = null;
            _judgmentSubmitted = false;
        }

        /// <summary>
        /// 绑定 LocalLockstep：设置真人掩码并接管该玩家的判定/行动输入钩子。
        /// 行动钩子仅接管真人槽（AI 槽由 LocalLockstep 掩码分流至 SimpleAI）；
        /// 旧模式（无掩码，如 ManualTestUI 全手动）下 ManualInputProvider 全接管。
        /// 重复 Bind 覆盖掩码与钩子，并清空未消费意图。
        /// </summary>
        public void Bind(LocalLockstep lockstep, int humanPlayerId)
        {
            // 与 AttachNetwork 互斥，最后一次调用决定模式；同实例交叉切换不可达
            // （每局新建 presenter），防御性清零
            _chipSink = null;
            _cmdSink = null;
            _networkActorProvider = null;
            _judgmentSubmitted = false;
            if (humanPlayerId < 0 || humanPlayerId >= lockstep.State.PlayerCount)
                throw new ArgumentException(
                    $"humanPlayerId {humanPlayerId} 超出玩家范围 [0, {lockstep.State.PlayerCount - 1}]",
                    nameof(humanPlayerId));
            _lockstep = lockstep;
            _humanPlayerId = humanPlayerId;
            _pendingJudgment = null;
            _pendingCommand = null;
            lockstep.HumanPlayers = new bool[lockstep.State.PlayerCount];
            lockstep.HumanPlayers[humanPlayerId] = true;
            lockstep.HumanJudgmentInput = GetHumanJudgmentInput;
            lockstep.ManualInputProvider = GetHumanActionInput;
        }

        /// <summary>暂存筹码分配意图（归属绑定玩家）：网络模式直达 chipSink；热座写 pending。
        /// 后提交覆盖先提交（热座）；网络模式每窗口一次提交由 UI 门控。</summary>
        public void SubmitChipAllocation(int rock, int scissors, int paper)
        {
            if (_chipSink != null)
            {
                var alloc = new ChipAllocation(_humanPlayerId)
                {
                    Rock = rock, Scissors = scissors, Paper = paper
                };
                _chipSink(alloc);
                _judgmentSubmitted = true;
                return;
            }
            _pendingJudgment = new ChipAllocation(_humanPlayerId)
            {
                Rock = rock, Scissors = scissors, Paper = paper
            };
        }

        /// <summary>暂存行动指令意图：网络模式直达 cmdSink；热座写 pending。
        /// 非绑定玩家的指令直接丢弃（防误用输入进入确定性流）。</summary>
        public void SubmitCommand(ICommand cmd)
        {
            if (_humanPlayerId >= 0 && cmd.PlayerId != _humanPlayerId)
                return;
            if (_cmdSink != null)
            {
                _cmdSink(cmd);
                return;
            }
            _pendingCommand = cmd;
        }

        // 不匹配时保留槽位等待（与 ManualTestUI 的全手动丢弃不同——掩码分流下
        // AI 不经此轮询，丢弃反而会吞掉真人刚提交的指令）
        private ChipAllocation? GetHumanJudgmentInput(int playerId)
        {
            if (_pendingJudgment != null && _pendingJudgment.Value.PlayerId == playerId)
            {
                ChipAllocation a = _pendingJudgment.Value;
                _pendingJudgment = null;
                return a;
            }
            return null;
        }

        // 不匹配时保留槽位等待（与 ManualTestUI 的全手动丢弃不同——掩码分流下
        // AI 不经此轮询，丢弃反而会吞掉真人刚提交的指令）
        private ICommand GetHumanActionInput(int playerId)
        {
            if (_pendingCommand != null && _pendingCommand.PlayerId == playerId)
            {
                ICommand cmd = _pendingCommand;
                _pendingCommand = null;
                return cmd;
            }
            return null;
        }
    }
}
