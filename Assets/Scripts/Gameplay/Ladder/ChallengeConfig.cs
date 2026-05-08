namespace Gameplay.Ladder
{
    /// <summary>一次挑战的配置。</summary>
    public sealed class ChallengeConfig
    {
        /// <summary>挑战主 seed（逐层角色抽签与层局种子均由此派生；全链路可复现）。</summary>
        public int Seed;

        /// <summary>层数上限（0 = LadderRules.MaxLayers；测试注入小塔加速）。</summary>
        public int LayerCap;

        /// <summary>单层回合护栏（0 = LadderRules.MaxRoundsPerLayer；测试注入小值触发护栏分支）。</summary>
        public int MaxRoundsPerLayer;
    }
}
