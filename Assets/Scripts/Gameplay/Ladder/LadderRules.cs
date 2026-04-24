namespace Gameplay.Ladder
{
    /// <summary>天梯·肉鸽挑战常量与纯函数（spec：纪录制段位 + 总量排名分）。</summary>
    public static class LadderRules
    {
        /// <summary>挑战层数上限（引擎 MaxPlayers=8 → 第 7 层 = 8 人局 1v7 封顶）。</summary>
        public const int MaxLayers = 7;

        /// <summary>单层最大 RunTurn 次数（超出判失败终止，防死局；测试用小值覆盖）。</summary>
        public const int MaxRoundsPerLayer = 400;

        /// <summary>挑战角色池：文档 15 角色 Def Id（排除 3=普通人无技能；Id 见 docs/项目介绍.md 7.1-7.15）。</summary>
        public static readonly int[] CharacterPool =
        {
            1, 2, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16
        };

        /// <summary>第 layer 层的玩家人数：1v1 起逐层 +1。</summary>
        public static int PlayerCountAtLayer(int layer) => layer + 1;

        /// <summary>单次挑战得分 = Σ(吃鸡层号 ×10)：连胜 s → 10·s(s+1)/2（1→10, 7→280）。</summary>
        public static long SingleRunScore(int streak)
        {
            long s = streak;
            return 10L * s * (s + 1) / 2;
        }

        /// <summary>段位索引 0..4：0=未定级 1-2=青铜 3-4=白银 5-6=黄金 7=登顶。</summary>
        public static int RankOf(int bestStreak)
        {
            if (bestStreak <= 0) return 0;
            if (bestStreak <= 2) return 1;
            if (bestStreak <= 4) return 2;
            if (bestStreak <= 6) return 3;
            return 4;
        }

        public static string RankName(int rankIndex)
        {
            switch (rankIndex)
            {
                case 0: return "未定级";
                case 1: return "青铜";
                case 2: return "白银";
                case 3: return "黄金";
                case 4: return "登顶";
                default: return "未知";
            }
        }
    }
}
