namespace Gameplay.Ladder
{
    /// <summary>一次挑战的结果（挑战已结算进档案，此对象供调用方展示/断言）。</summary>
    public sealed class ChallengeResult
    {
        /// <summary>吃鸡层数（连胜）；0 = 第 1 层未过。</summary>
        public int Streak;

        /// <summary>本次得分 = LadderRules.SingleRunScore(Streak)。</summary>
        public long Score;

        /// <summary>是否登顶（Streak == 本次层数上限）。</summary>
        public bool Won;

        /// <summary>失败层号 1..上限（Won 时 0）。</summary>
        public int FailedAtLayer;

        /// <summary>是否因单层回合护栏超限终止（槽 0 未死但局未决）。</summary>
        public bool TimedOut;
    }
}
