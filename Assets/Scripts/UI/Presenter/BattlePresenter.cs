using System.Collections.Generic;
using System.Text;
using Core;
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
    /// 读 WorldState 构建快照；消费输入意图（输入意图在后续任务扩展）。
    /// </summary>
    public class BattlePresenter
    {
        private const int LogCapacity = 200;
        private const int LogTailCount = 8;

        private readonly WorldState _state;
        private readonly Queue<string> _log = new Queue<string>(LogCapacity);
        private readonly string[] _handNames = { "石头", "剪刀", "布" };
#pragma warning disable CS0649 // Task 4 的 Bind 将赋值；未绑定时 CurrentActorPlayerId 返回 -1
        private LocalLockstep _lockstep;
#pragma warning restore CS0649

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

            if (_state.HasJudgmentResult && _state.Phase == TurnPhase.ActionPhase)
                snap.JudgmentSummary = BuildJudgmentSummary();

            string[] allLog = _log.ToArray();
            for (int i = System.Math.Max(0, allLog.Length - LogTailCount);
                 i < allLog.Length; i++)
                snap.LogTail.Add(allLog[i]);
            return snap;
        }

        /// <summary>当前行动者（未绑定 lockstep 时 -1）。后续任务的 Bind 赋 _lockstep。</summary>
        private int CurrentActorPlayerId()
            => _lockstep != null ? _lockstep.CurrentActorPlayerId : -1;

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
                EnqueueLog($"{c?.Name ?? "?"} 被 {killer?.Name ?? "?"} 击杀");
            });
            events.Subscribe(GameplayEventTypes.DamageDealt, 0, (ref GameplayEvent e) =>
            {
                var src = _state.GetCharacter(e.SourceId);
                var tgt = _state.GetCharacter(e.TargetId);
                EnqueueLog($"{src?.Name ?? "?"} → {tgt?.Name ?? "?"} : {e.Magnitude:F1} 伤害");
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
    }
}
