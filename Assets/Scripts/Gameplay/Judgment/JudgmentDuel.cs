using System;
using Core;
using Gameplay.Characters;

namespace Gameplay.Judgment
{
    /// <summary>
    /// 单对单判定：双方随机筹码对决，复用 JudgmentSystem。
    /// 确定性：筹码来自 state.Random（DeterministicRandom），同 seed 同结果。
    /// </summary>
    public static class JudgmentDuel
    {
        public static bool Resolve(WorldState state, Character a, Character b)
        {
            if (a == null || b == null) return false;
            Span<ChipAllocation> alloc = stackalloc ChipAllocation[2];
            alloc[0] = new ChipAllocation(a.PlayerId);
            alloc[0].SetRandom(state.Random);
            alloc[1] = new ChipAllocation(b.PlayerId);
            alloc[1].SetRandom(state.Random);
            int flat = 0;
            JudgmentResult r = new JudgmentSystem().Resolve(alloc, state.Random, ref flat);
            return r.Roles[0] == JudgmentRole.Winner;
        }
    }
}
