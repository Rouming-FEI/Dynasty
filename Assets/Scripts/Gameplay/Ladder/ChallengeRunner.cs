using System;
using AI;
using Core;
using Gameplay.Characters;
using Network.Local;

namespace Gameplay.Ladder
{
    /// <summary>
    /// 天梯挑战驱动：逐层完整吃鸡局（第 k 层 = k+1 人局，1v1 起、8 人局封顶），
    /// 槽 0 吃鸡过层、死亡即败、护栏超时按失败计；每层全新 WorldState（无层间成长）。
    /// 双路径共用状态机内核：Run()（自动，全槽 SimpleAI）与会话模式（真人宿主——
    /// BeginHumanRun/ResumeHumanRun 取层 → 宿主跑局 → ReportHumanLayerEnd 回报判定）。
    /// 结算单点 CompleteRun()：统计只在终局结算一次；终局后 Report 抛异常（防二次结算）。
    /// 真人接入契约：槽 0 决策注入点 = drawDefs 抽签结果的槽位本身，Runner 内部不写死槽 0。
    /// </summary>
    public sealed class ChallengeRunner
    {
        private readonly ChallengeConfig _config;
        private readonly IProfileStore _store;
        private readonly Func<int, int, DeterministicRandom, CharacterDef[]> _drawDefs;

        // 单次挑战运行状态（Run/BeginHumanRun 周期内有效）
        private DeterministicRandom _rng;
        private int _layerCap;
        private int _roundCap;
        private int _streak;
        private ChallengeRunSession _session;

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

        /// <summary>跑一次挑战（自动，从头到失败或登顶），结算并保存档案，返回结果。</summary>
        public ChallengeResult Run()
        {
            StartInternal(_config.Seed);
            while (_session.IsActive)
            {
                int layer = _session.CurrentLayer;
                LayerRunInfo info = RunLayerGame(LadderRules.PlayerCountAtLayer(layer),
                    _session.CurrentLayerSeed, _session.CurrentDefs, _roundCap);
                JudgeAndAdvance(info);
            }
            return _session.Result;
        }

        /// <summary>会话模式：开始新挑战并准备第 1 层（真人宿主跑局后 ReportHumanLayerEnd）。</summary>
        public ChallengeRunSession BeginHumanRun(int seed)
        {
            StartInternal(seed);
            return _session;
        }

        /// <summary>会话模式：从存档层续跑（层间存档点）——补消费中间层抽签/层 seed
        /// （层 1 已由 StartInternal 预备，重放 2..atLayer-1），streak 起点 = atLayer-1
        /// （存档点意味着此前各层已吃鸡）。atLayer 越界抛 ArgumentOutOfRange。</summary>
        public ChallengeRunSession ResumeHumanRun(int seed, int atLayer)
        {
            if (atLayer < 1 || atLayer > LadderRules.MaxLayers)
                throw new ArgumentOutOfRangeException(nameof(atLayer), atLayer,
                    $"续跑层必须在 1..LadderRules.MaxLayers（{LadderRules.MaxLayers}）内");
            StartInternal(seed);
            _streak = atLayer - 1;
            if (atLayer > 1)
            {
                // StartInternal 已把流推进到层 1 之后（PrepareNextLayer(1) 消费 draw(1)+seed）；
                // 续跑需补消费 2..atLayer-1 各层，再备 atLayer 层（atLayer==1 等价 Begin，直接返回）。
                for (int layer = 2; layer < atLayer; layer++)
                {
                    _drawDefs(layer, LadderRules.PlayerCountAtLayer(layer), _rng);
                    _rng.Next(int.MaxValue);
                }
                PrepareNextLayer(atLayer);
            }
            return _session;
        }

        /// <summary>会话模式：回报当前层局终态 → 吃鸡判定/推进/终局结算。
        /// 挑战未就绪或已结束时调用抛 InvalidOperationException（协议错误响亮失败，防二次结算）。</summary>
        public ChallengeRunSession ReportHumanLayerEnd(LayerRunInfo info)
        {
            if (_session == null || !_session.IsActive)
                throw new InvalidOperationException(
                    "ReportHumanLayerEnd 在挑战未开始或已结束时被调用（每层只能回报一次）");
            JudgeAndAdvance(info);
            return _session;
        }

        /// <summary>开始一次挑战：解析上限/护栏、重置统计、准备层 1（会话对象创建）。</summary>
        private void StartInternal(int seed)
        {
            _rng = new DeterministicRandom(seed);
            _layerCap = _config.LayerCap > 0 ? _config.LayerCap : LadderRules.MaxLayers;
            if (_layerCap > LadderRules.MaxLayers)
                throw new ArgumentOutOfRangeException(nameof(_config.LayerCap),
                    _config.LayerCap, $"层数上限不能超过 LadderRules.MaxLayers（{LadderRules.MaxLayers}）");
            _roundCap = _config.MaxRoundsPerLayer > 0
                ? _config.MaxRoundsPerLayer : LadderRules.MaxRoundsPerLayer;
            _streak = 0;
            _session = new ChallengeRunSession();
            PrepareNextLayer(1);
        }

        /// <summary>抽签 + 取层局 seed，填充会话为待跑层。</summary>
        private void PrepareNextLayer(int layer)
        {
            CharacterDef[] defs = _drawDefs(layer, LadderRules.PlayerCountAtLayer(layer), _rng);
            if (defs == null || defs.Length != LadderRules.PlayerCountAtLayer(layer))
                throw new ArgumentException($"drawDefs 层 {layer} 返回长度错误");
            _session.CurrentDefs = defs;
            _session.CurrentLayerSeed = _rng.Next(int.MaxValue);
            _session.CurrentLayer = layer;
            _session.IsActive = true;
            _session.IsComplete = false;
            _session.Result = null;
        }

        /// <summary>吃鸡/失败三分判定 + 推进或终局结算（自动与会话共用）。</summary>
        private void JudgeAndAdvance(LayerRunInfo info)
        {
            if (info.Slot0Alive && info.Phase == TurnPhase.GameOver)
            {
                _streak++;
                if (_session.CurrentLayer >= _layerCap)
                {
                    CompleteRun(0, false);      // 登顶：无失败层
                    return;
                }
                PrepareNextLayer(_session.CurrentLayer + 1);
                return;
            }
            // 失败：FailedAtLayer 恒 = 失败层；TimedOut 仅当槽 0 未死但局未决（护栏超时）
            CompleteRun(_session.CurrentLayer, info.Slot0Alive && info.Phase != TurnPhase.GameOver);
        }

        /// <summary>终局：结算档案（单点）并填充 Result。failedAtLayer：失败层号（登顶传 0）。</summary>
        private void CompleteRun(int failedAtLayer, bool timedOut)
        {
            long score = LadderRules.SingleRunScore(_streak);
            LadderProfile updated = _store.Load().Apply(_streak, score);
            _store.Save(updated);

            _session.Result = new ChallengeResult
            {
                Streak = _streak,
                Score = score,
                Won = _streak == _layerCap,
                FailedAtLayer = failedAtLayer,
                TimedOut = timedOut
            };
            _session.CurrentDefs = null;
            _session.CurrentLayerSeed = 0;
            _session.IsActive = false;
            _session.IsComplete = true;
        }

        /// <summary>层局跑完后的快照信息（公开：自动跑局/会话回报共用形态；Rounds 仅自动模式有意义）。</summary>
        public struct LayerRunInfo
        {
            public TurnPhase Phase;
            public bool Slot0Alive;
            public int Rounds;
        }

        /// <summary>跑一层完整局：逐回合驱动到 GameOver 或护栏；槽 0 中途死亡立即退出（局未决）。
        /// 隐含前提：roundCap 必须小于 GameConfig.MaxTurns（999）——否则回合上限式 GameOver
        /// 可能多活者并存，`Slot0Alive && GameOver` 不再等价于唯一存活吃鸡（默认 400 安全）。</summary>
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

    /// <summary>一次挑战的会话视图（Runner 内部推进字段；宿主只读 + 按序回报）。</summary>
    public sealed class ChallengeRunSession
    {
        /// <summary>当前（待跑/最后）层号。</summary>
        public int CurrentLayer;

        /// <summary>待跑层槽位 defs（终局后为 null）。</summary>
        public CharacterDef[] CurrentDefs;

        /// <summary>待跑层层局种子（热座 WorldState 用；终局后 0）。</summary>
        public int CurrentLayerSeed;

        /// <summary>是否有层待跑（IsComplete 为 false 时宿主应跑当前层并回报）。</summary>
        public bool IsActive;

        /// <summary>是否已终局（Result 非 null，档案已结算）。</summary>
        public bool IsComplete;

        /// <summary>终局结果（IsComplete 前为 null）。</summary>
        public ChallengeResult Result;
    }
}
