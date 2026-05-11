namespace Gameplay.Ladder
{
    /// <summary>天梯档案（不可变快照语义：更新 = Apply 产生新实例，不就地修改）。</summary>
    public struct LadderProfile
    {
        /// <summary>生涯最佳连胜（段位依据，纪录制）。</summary>
        public int BestStreak;

        /// <summary>生涯总分 = Σ 历次挑战单次得分（总量排名键）。</summary>
        public long TotalScore;

        /// <summary>挑战次数。</summary>
        public int Attempts;

        /// <summary>最近一次挑战连胜（展示用）。</summary>
        public int LastStreak;

        /// <summary>最近一次挑战得分（展示用）。</summary>
        public long LastScore;

        /// <summary>是否有进行中挑战（门控字段——struct 零值默认 false = 无，无歧义哨兵）。</summary>
        public bool HasActiveRun;

        /// <summary>进行中挑战主 seed（HasActiveRun 为 false 时无意义）。</summary>
        public int ActiveSeed;

        /// <summary>进行中挑战当前层（1..MaxLayers；HasActiveRun 为 false 时无意义）。</summary>
        public int ActiveLayer;

        /// <summary>段位索引（0=未定级 .. 4=登顶），由 BestStreak 派生。</summary>
        public int Rank => LadderRules.RankOf(BestStreak);

        public string RankName => LadderRules.RankName(Rank);

        /// <summary>结算一次挑战：bestStreak 取 max、总分累加、次数 +1、last 字段覆盖；返回新实例。</summary>
        public LadderProfile Apply(int streak, long score)
        {
            var p = this;
            if (streak > p.BestStreak) p.BestStreak = streak;
            p.TotalScore += score;
            p.Attempts += 1;
            p.LastStreak = streak;
            p.LastScore = score;
            return p;
        }
    }
}
