using AI;
using Core;
using Core.Command;
using Core.Replay;
using Gameplay.Characters;
using Gameplay.Judgment;
using Gameplay.Movement;
using Network.Local;
using NUnit.Framework;

namespace Dynasty.Tests.Replay
{
    public class ReplayRoundTripTests
    {
        /// <summary>跑一局 AI 对战并录制，返回 (lockstep, recorder, log)。</summary>
        private static (LocalLockstep, ReplayRecorder, ReplayLog) RecordGame(int seed, int playerCount = 4)
        {
            var state = new WorldState(playerCount, seed);
            var ais = new SimpleAI[playerCount];
            for (int i = 0; i < ais.Length; i++) ais[i] = new SimpleAI(i);
            var lockstep = new LocalLockstep(state, ais);
            lockstep.StartGame();
            var recorder = ReplayRecorder.Attach(lockstep, seed, new int[playerCount]); // 默认轮换
            int guard = 0;
            while (state.Phase != TurnPhase.GameOver && guard++ < GameConfig.MaxTurns)
                lockstep.RunTurn();
            var log = recorder.Finish();
            return (lockstep, recorder, log);
        }

        [Test]
        public void Record_CapturesChipsActionsAndEvents()
        {
            var (lockstep, recorder, log) = RecordGame(42);
            Assert.That(log.Chips.Count, Is.GreaterThan(0), "应记录至少一轮判定");
            Assert.That(log.Actions.Count, Is.GreaterThan(0), "应记录行动指令");
            Assert.That(log.TotalTicks, Is.GreaterThan(0));
            Assert.That(log.TickHashes.Length, Is.EqualTo(log.TotalTicks), "哈希检查点应与 Tick 数对齐");
            Assert.That(log.Events.Count, Is.GreaterThan(0), "应记录事件时间线");
            Assert.That(log.CharacterDefIds, Is.Not.Null);
            Assert.That(lockstep.State.Phase, Is.EqualTo(TurnPhase.GameOver), "录制局应打到 GameOver");
        }

        [Test]
        public void Record_TickHashesMatchLiveState()
        {
            var (lockstep, recorder, log) = RecordGame(7);
            // 录制结束时终态哈希应等于最后检查点
            Assert.That(log.TickHashes[log.TotalTicks - 1], Is.EqualTo(lockstep.State.ComputeStateHash()));
        }

        [Test]
        public void Finish_DetachesOwnHooks_PreservesExternalJudgmentCollected()
        {
            var state = new WorldState(4, 1);
            var ais = new SimpleAI[4];
            for (int i = 0; i < ais.Length; i++) ais[i] = new SimpleAI(i);
            var lockstep = new LocalLockstep(state, ais);
            lockstep.StartGame();
            // Attach 前设外部哨兵：录制器从未挂此钩子，Finish 不得误清
            bool sentinelCalled = false;
            lockstep.OnJudgmentCollected = (pid, alloc, isHuman) => sentinelCalled = true;
            var recorder = ReplayRecorder.Attach(lockstep, 1, new int[4]);
            int guard = 0;
            while (state.Phase != TurnPhase.GameOver && guard++ < GameConfig.MaxTurns)
                lockstep.RunTurn();
            recorder.Finish();
            sentinelCalled = false;
            lockstep.OnJudgmentCollected(0, default, false);
            Assert.That(sentinelCalled, Is.True, "外部 OnJudgmentCollected 应未被 Finish 误清");
            Assert.That(lockstep.OnJudgmentCommitted, Is.Null);
            Assert.That(lockstep.OnActionCommand, Is.Null);
            Assert.That(lockstep.State.Events.RecorderHook, Is.Null);
            Assert.That(lockstep.State.OnTickAdvanced, Is.Null);
        }

        [Test]
        public void Finish_IsIdempotent_ReturnsSameLog()
        {
            var (lockstep, recorder, log) = RecordGame(1);
            var log2 = recorder.Finish();
            Assert.That(log2, Is.SameAs(log), "二次 Finish 应返回同一 ReplayLog 实例");
            // 钩子不重复清（无异常），仍为 null
            Assert.That(lockstep.OnJudgmentCommitted, Is.Null);
            Assert.That(lockstep.OnActionCommand, Is.Null);
            Assert.That(lockstep.State.Events.RecorderHook, Is.Null);
            Assert.That(lockstep.State.OnTickAdvanced, Is.Null);
        }

        [Test]
        public void Record_CapturesHumanPlayersMask()
        {
            var state = new WorldState(2, 11);
            var ais = new SimpleAI[2];
            for (int i = 0; i < ais.Length; i++) ais[i] = new SimpleAI(i);
            var lockstep = new LocalLockstep(state, ais);
            lockstep.HumanPlayers = new[] { true, false };
            lockstep.HumanJudgmentInput = pid =>
            {
                var a = new ChipAllocation(pid);
                a.Set(2, 1, 1);
                return a;
            };
            lockstep.ManualInputProvider = pid => new WasteAPCommand(state.CurrentTick, pid);
            lockstep.StartGame();
            var recorder = ReplayRecorder.Attach(lockstep, 11, new int[2]);
            int guard = 0;
            while (state.Phase != TurnPhase.GameOver && guard++ < GameConfig.MaxTurns)
                lockstep.RunTurn();
            var log = recorder.Finish();
            Assert.That(log.HumanPlayersMask.Length, Is.EqualTo(2));
            Assert.That(log.HumanPlayersMask[0], Is.True, "0 号位真人掩码应录制");
            Assert.That(log.HumanPlayersMask[1], Is.False);
        }

        [Test]
        public void Record_VoidRejudgeRounds_SnapshotPerRound()
        {
            var state = new WorldState(4, 5);
            var ais = new SimpleAI[4];
            for (int i = 0; i < ais.Length; i++) ais[i] = new SimpleAI(i);
            var lockstep = new LocalLockstep(state, ais);
            lockstep.JudgmentInputProvider = pid =>
            {
                var a = new ChipAllocation(pid);
                a.SetDefault(); // 全员 1-1-1 → 全局平
                return a;
            };
            lockstep.ManualInputProvider = pid => null; // 行动阶段首位行动者处暂停
            lockstep.StartGame();
            var recorder = ReplayRecorder.Attach(lockstep, 5, new int[4]);
            int guard = 0;
            while (state.Phase != TurnPhase.ActionPhase
                   && state.Phase != TurnPhase.GameOver && guard++ < 20)
                lockstep.RunTurn();
            var log = recorder.Finish();
            Assert.That(state.Phase, Is.EqualTo(TurnPhase.ActionPhase), "应暂停在行动阶段");
            Assert.That(log.Chips.Count, Is.EqualTo(2), "两次全平（作废+保底）各占一轮快照");
            for (int round = 0; round < log.Chips.Count; round++)
            {
                ChipAllocation[] allocs = log.Chips[round].Allocations;
                Assert.That(allocs.Length, Is.EqualTo(4));
                for (int i = 0; i < allocs.Length; i++)
                {
                    Assert.That(allocs[i].Rock, Is.EqualTo(1), $"round={round} pid={i} Rock");
                    Assert.That(allocs[i].Scissors, Is.EqualTo(1), $"round={round} pid={i} Scissors");
                    Assert.That(allocs[i].Paper, Is.EqualTo(1), $"round={round} pid={i} Paper");
                }
            }
        }

        [Test]
        public void Replay_AiGame_HashesMatchEveryTick()
        {
            var (lockstep, recorder, log) = RecordGame(42);
            var player = new ReplayPlayer(log);
            int guard = 0;
            while (!player.Finished && !player.HasDeviation && guard++ < 10000)
                player.StepTick();
            Assert.That(player.HasDeviation, Is.False,
                $"回放偏差发生在 Tick {player.DeviationTick}");
            Assert.That(player.Finished, Is.True, "回放应推进到录制终点");
            Assert.That(player.State.CurrentTurn, Is.EqualTo(lockstep.State.CurrentTurn));
            Assert.That(player.State.Phase, Is.EqualTo(lockstep.State.Phase));
        }

        [Test]
        public void Replay_AnotherSeed_HashesMatch()
        {
            var (lockstep, recorder, log) = RecordGame(2024);
            var player = new ReplayPlayer(log);
            int guard = 0;
            while (!player.Finished && !player.HasDeviation && guard++ < 10000)
                player.StepTick();
            Assert.That(player.HasDeviation, Is.False,
                $"回放偏差发生在 Tick {player.DeviationTick}");
        }

        [Test]
        public void Replay_HumanMixedGame_HashesMatch()
        {
            // 1 真人（Player 0）+ 3 AI 的混合局
            var state = new WorldState(4, 99);
            var ais = new SimpleAI[4];
            for (int i = 0; i < ais.Length; i++) ais[i] = new SimpleAI(i);
            var lockstep = new LocalLockstep(state, ais);
            lockstep.HumanPlayers = new[] { true, false, false, false };
            // 真人判定：固定 2-1-1；真人行动：固定 WasteAP
            lockstep.HumanJudgmentInput = pid =>
            {
                var a = new ChipAllocation(pid);
                a.Set(2, 1, 1);
                return a;
            };
            lockstep.ManualInputProvider = pid => new WasteAPCommand(state.CurrentTick, pid);
            lockstep.StartGame();
            var recorder = ReplayRecorder.Attach(lockstep, 99, new int[4]);
            int guard = 0;
            while (state.Phase != TurnPhase.GameOver && guard++ < GameConfig.MaxTurns)
                lockstep.RunTurn();
            var log = recorder.Finish();
            Assert.That(log.HumanPlayersMask[0], Is.True, "真人掩码应被记录");

            var player = new ReplayPlayer(log);
            guard = 0;
            while (!player.Finished && !player.HasDeviation && guard++ < 10000)
                player.StepTick();
            Assert.That(player.HasDeviation, Is.False,
                $"混合局回放偏差发生在 Tick {player.DeviationTick}");
        }

        [Test]
        public void Replay_TamperedSeed_ReportsDeviation()
        {
            var (lockstep, recorder, log) = RecordGame(42);
            log.Seed = 43; // 篡改种子 → 回放首 Tick 即偏差
            var player = new ReplayPlayer(log);
            int guard = 0;
            while (!player.Finished && !player.HasDeviation && guard++ < 10000)
                player.StepTick();
            Assert.That(player.HasDeviation, Is.True, "篡改种子应报告偏差");
            Assert.That(player.DeviationTick, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void Replay_TamperedHumanAction_ReportsDeviation()
        {
            var state = new WorldState(2, 3);
            var ais = new SimpleAI[2];
            for (int i = 0; i < ais.Length; i++) ais[i] = new SimpleAI(i);
            var lockstep = new LocalLockstep(state, ais);
            lockstep.HumanPlayers = new[] { true, false };
            lockstep.HumanJudgmentInput = pid =>
            {
                var a = new ChipAllocation(pid);
                a.Set(3, 0, 0); // 全石头：2 人局 1-1-1 恒 Neutral（winner==loser==1）永 0 AP 永不行动
                return a;
            };
            lockstep.ManualInputProvider = pid =>
            {
                Character c = state.GetCharacterByPlayer(pid);
                var conns = state.Locations.GetConnectedLocations(c.CurrentLocationId);
                return conns.Length > 0
                    ? (ICommand)new MoveCommand(state.CurrentTick, pid, conns[0])
                    : new WasteAPCommand(state.CurrentTick, pid);
            };
            lockstep.StartGame();
            var recorder = ReplayRecorder.Attach(lockstep, 3, new int[2]);
            int guard = 0;
            while (state.Phase != TurnPhase.GameOver && guard++ < GameConfig.MaxTurns)
                lockstep.RunTurn();
            var log = recorder.Finish();

            // 篡改第一条真人行动命令的 Move 目标地点
            bool tampered = false;
            for (int i = 0; i < log.Actions.Count; i++)
            {
                var a = log.Actions[i];
                if (a.Source == 1 && a.Kind == ReplayActionKind.Move)
                {
                    log.Actions[i] = new ReplayActionRecord
                    {
                        Tick = a.Tick, PlayerId = a.PlayerId, Kind = a.Kind, Source = a.Source,
                        IntArg1 = (a.IntArg1 + 1) % GameConfig.MaxLocations
                    };
                    tampered = true;
                    break;
                }
            }
            Assume.That(tampered, Is.True, "2 人局真人应至少移动一次");

            var player = new ReplayPlayer(log);
            guard = 0;
            while (!player.Finished && !player.HasDeviation && guard++ < 10000)
                player.StepTick();
            Assert.That(player.HasDeviation, Is.True, "篡改真人行动应报告偏差");
        }

        [Test]
        public void Replay_StepCommand_AdvancesAndConsumesHumanAction()
        {
            var state = new WorldState(2, 3);
            var ais = new SimpleAI[2];
            for (int i = 0; i < ais.Length; i++) ais[i] = new SimpleAI(i);
            var lockstep = new LocalLockstep(state, ais);
            lockstep.HumanPlayers = new[] { true, false };
            lockstep.HumanJudgmentInput = pid =>
            {
                var a = new ChipAllocation(pid);
                a.Set(3, 0, 0);
                return a;
            };
            lockstep.ManualInputProvider = pid =>
            {
                Character c = state.GetCharacterByPlayer(pid);
                var conns = state.Locations.GetConnectedLocations(c.CurrentLocationId);
                return conns.Length > 0
                    ? (ICommand)new MoveCommand(state.CurrentTick, pid, conns[0])
                    : new WasteAPCommand(state.CurrentTick, pid);
            };
            lockstep.StartGame();
            var recorder = ReplayRecorder.Attach(lockstep, 3, new int[2]);
            int guard = 0;
            while (state.Phase != TurnPhase.GameOver && guard++ < GameConfig.MaxTurns)
                lockstep.RunTurn();
            var log = recorder.Finish();

            var player = new ReplayPlayer(log);
            int tickBefore = player.State.CurrentTick;
            Assert.That(player.StepCommand(), Is.True, "StepCommand 应推进到下一条真人行动");
            Assert.That(player.State.CurrentTick, Is.GreaterThan(tickBefore), "消费真人行动后 Tick 应推进");
            Assert.That(player.HasDeviation, Is.False);
        }
    }
}
