namespace Gameplay.Items
{
    using Core;
    using Core.GAS;
    using Gameplay.Characters;
    using Gameplay.Judgment;
    using System;

    /// <summary>
    /// 核弹判定发射系统。
    /// 流程：收集全员筹码分配 → 嵌入RPS判定 → 发射者在胜者列表则成功。
    /// 成功时对发射者以外所有玩家造成 3 点真实伤害，藏匿处(6)免疫。
    /// </summary>
    public static class NukeSystem
    {
        /// <summary>
        /// 发射核弹：嵌入全员 RPS 判定，发射者在胜者列表 → 成功并对全员造成 3 真实伤害。
        /// </summary>
        /// <returns>true if launch succeeded, false if launcher lost the judgment</returns>
        public static bool Launch(WorldState state, Character launcher)
        {
            int playerCount = state.PlayerCount;

            // 1. Collect chip allocations from all players (AI auto-allocate via SetRandom)
            Span<ChipAllocation> allocations = stackalloc ChipAllocation[playerCount];
            for (int i = 0; i < playerCount; i++)
            {
                allocations[i] = new ChipAllocation(i);
                allocations[i].SetRandom(state.Random);
            }

            // 2. Run embedded judgment
            JudgmentSystem judge = new JudgmentSystem();
            int flatCount = 0;
            JudgmentResult result = judge.Resolve(allocations, state.Random, ref flatCount);

            // 3. Check if launcher is in winner list
            bool launcherWon = result.Roles[launcher.PlayerId] == JudgmentRole.Winner;
            if (!launcherWon) return false;

            // 4. Deal 3 True damage to ALL players except launcher and hideout occupants
            for (int i = 0; i < playerCount; i++)
            {
                Character target = state.Characters[i];
                if (target == null || target.IsDead) continue;
                if (target.PlayerId == launcher.PlayerId) continue; // skip launcher
                if (target.CurrentLocationId == 6) continue; // hideout immune

                DamagePipeline.ApplyDamage(state, launcher.Id, target.Id, 3f, DamageType.True);
            }

            return true;
        }
    }
}
