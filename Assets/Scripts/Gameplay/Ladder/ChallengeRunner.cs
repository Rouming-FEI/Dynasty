using System;
using AI;
using Core;
using Gameplay.Characters;
using Network.Local;

namespace Gameplay.Ladder
{
    /// <summary>
    /// 天梯挑战驱动（P7 下半 spec）：逐层完整吃鸡局——第 k 层 = k+1 人局（1v1 起、
    /// 8 人局封顶）；槽 0 吃鸡过层、中途死亡立即终止、护栏超时按失败计。
    /// 每层全新 WorldState（无层间成长）；全槽 SimpleAI 托管。
    /// 真人接入契约（UI 阶段）：槽 0 决策注入点 = drawDefs 抽签结果的槽位本身，
    /// Runner 内部不得写死槽 0 为 AI——本阶段默认全 AI。
    /// </summary>
    public sealed class ChallengeRunner
    {
        private readonly ChallengeConfig _config;
        private readonly IProfileStore _store;
        private readonly Func<int, int, DeterministicRandom, CharacterDef[]> _drawDefs;

        /// <summary>drawDefs(layer, playerCount, 派生随机流) → 该层全部槽位 Def；null = 默认角色池抽签。</summary>
        public ChallengeRunner(ChallengeConfig config, IProfileStore store,
            Func<int, int, DeterministicRandom, CharacterDef[]> drawDefs = null)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (store == null) throw new ArgumentNullException(nameof(store));
            _config = config;
            _store = store;
            _drawDefs = drawDefs ?? DefaultDraw;
        }

        /// <summary>跑一次挑战（从头到失败或登顶），结算并保存档案，返回结果。</summary>
        public ChallengeResult Run()
        {
            var rng = new DeterministicRandom(_config.Seed);
            int layerCap = _config.LayerCap > 0 ? _config.LayerCap : LadderRules.MaxLayers;
            int roundCap = _config.MaxRoundsPerLayer > 0
                ? _config.MaxRoundsPerLayer : LadderRules.MaxRoundsPerLayer;

            int streak = 0;
            int failedAt = 0;
            bool timedOut = false;

            for (int layer = 1; layer <= layerCap; layer++)
            {
                CharacterDef[] defs = _drawDefs(layer, LadderRules.PlayerCountAtLayer(layer), rng);
                if (defs == null || defs.Length != LadderRules.PlayerCountAtLayer(layer))
                    throw new ArgumentException($"drawDefs 层 {layer} 返回长度错误");
                int layerSeed = rng.Next(int.MaxValue);
                LayerRunInfo info = RunLayerGame(LadderRules.PlayerCountAtLayer(layer),
                    layerSeed, defs, roundCap);

                if (info.Slot0Alive && info.Phase == TurnPhase.GameOver)
                {
                    streak++;
                    continue;
                }
                failedAt = layer;
                timedOut = info.Slot0Alive && info.Phase != TurnPhase.GameOver;
                break;
            }

            long score = LadderRules.SingleRunScore(streak);
            LadderProfile updated = _store.Load().Apply(streak, score);
            _store.Save(updated);

            return new ChallengeResult
            {
                Streak = streak,
                Score = score,
                Won = streak == layerCap,
                FailedAtLayer = failedAt,
                TimedOut = timedOut
            };
        }

        /// <summary>层局跑完后的快照信息（公开：8 人局上限/收敛性测试直接调用 RunLayerGame）。</summary>
        public struct LayerRunInfo
        {
            public TurnPhase Phase;
            public bool Slot0Alive;
            public int Rounds;
        }

        /// <summary>跑一层完整局：逐回合驱动到 GameOver 或护栏；槽 0 中途死亡立即退出（局未决）。</summary>
        public static LayerRunInfo RunLayerGame(int playerCount, int seed,
            CharacterDef[] defs, int roundCap)
        {
            var state = new WorldState(playerCount, seed);
            var ais = new SimpleAI[playerCount];
            for (int i = 0; i < playerCount; i++) ais[i] = new SimpleAI(i);
            var lockstep = new LocalLockstep(state, ais);
            lockstep.StartGame(defs);

            int rounds = 0;
            while (state.Phase != TurnPhase.GameOver && rounds < roundCap)
            {
                if (!IsAlive(state, 0)) break;      // 槽 0 死亡早退（不等整局结算）
                if (!lockstep.RunTurn()) break;     // false = GameOver（含全灭）
                rounds++;
            }
            return new LayerRunInfo
            {
                Phase = state.Phase,
                Slot0Alive = IsAlive(state, 0),
                Rounds = rounds
            };
        }

        /// <summary>默认抽签：从 LadderRules.CharacterPool 依次为每槽随机取 Def（同场允许重复）。</summary>
        private static CharacterDef[] DefaultDraw(int layer, int playerCount, DeterministicRandom rng)
        {
            var defs = new CharacterDef[playerCount];
            for (int i = 0; i < playerCount; i++)
                defs[i] = CharacterDefCatalog.Get(
                    LadderRules.CharacterPool[rng.Next(LadderRules.CharacterPool.Length)]);
            return defs;
        }

        private static bool IsAlive(WorldState state, int playerId)
        {
            Character c = state.GetCharacterByPlayer(playerId);
            return c != null && !c.IsDead;
        }
    }
}
