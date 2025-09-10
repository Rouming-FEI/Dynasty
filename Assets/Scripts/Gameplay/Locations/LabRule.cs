using System;
using Core;
using Core.GAS;
using Gameplay.Characters;
using Gameplay.Judgment;

namespace Gameplay.Locations
{
    /// <summary>
    /// 实验室(7)规则：大门开关 + 深度潮汐子设施。
    ///
    /// 大门：初始锁定（DoorOpen = false），关闭时拒绝角色进入（CanEnter），
    /// 开/关门通过 ToggleDoorCommand 消耗 1 AP。
    ///
    /// 深度潮汐：开关开启后每 2 回合触发一次室内 vs 室外的群体双向判定，
    /// 输方每人受 1 点法术效果伤（DamageType.Effect，无视抗性、可被护盾抵挡）：
    /// - 阈值器完好   → 室内输方受伤
    /// - 阈值器已拆除 → 室外输方受伤（拆除后不可装回）
    /// 实验室被毁的全场互判机制留待施工锤拆除地点整合后实现（P1 跳过）。
    /// </summary>
    public class LabRule : LocationRule
    {
        public const int LabLocationId = 7;

        /// <summary>大门是否打开。false = 关闭（初始锁定，拒绝进入）。</summary>
        public bool DoorOpen;

        /// <summary>深度潮汐开关。false = 关闭（不计数、不判定）。</summary>
        public bool SwitchOn;

        /// <summary>阈值器是否已被拆除。拆除后不可装回。</summary>
        public bool ThresholdRemoved;

        /// <summary>潮汐 2 回合周期计数器（从开关开启后计数）。</summary>
        public int TideTurnsElapsed;

        public LabRule() { LocationId = LabLocationId; }

        public void ToggleDoor() => DoorOpen = !DoorOpen;

        /// <summary>切换潮汐开关。任一方向切换都重置周期计数（"每 2 回合"从开启时刻起算）。</summary>
        public void ToggleSwitch()
        {
            SwitchOn = !SwitchOn;
            TideTurnsElapsed = 0;
        }

        /// <summary>拆除阈值器。单向操作，装不回去。</summary>
        public void RemoveThreshold() => ThresholdRemoved = true;

        /// <summary>大门关闭时拒绝进入（MoveCommand 在扣 AP 前调用，拒绝不消耗 AP）。</summary>
        public override bool CanEnter(WorldState state, Character c) => DoorOpen;

        public override void OnTurnEnd(WorldState state)
        {
            if (!SwitchOn) return;

            TideTurnsElapsed++;
            if (TideTurnsElapsed < 2) return;
            TideTurnsElapsed = 0;

            RunTidePulse(state);
        }

        /// <summary>
        /// 深度潮汐脉冲：室内 vs 室外群体双向判定。
        /// 每名存活角色用确定性 RNG 随机分配 3 枚筹码，按所在侧聚合 R/S/P 总量，
        /// 各侧取筹码最多的手牌为该侧出牌（平手确定性随机决定），两侧 RPS 对决。
        /// 同手牌 = 平局，无人受伤。任一侧无人时脉冲落空。
        /// P1 简化：聚合为一次群体判定而非 O(n²) 逐对判定。零堆分配。
        /// </summary>
        private void RunTidePulse(WorldState state)
        {
            int indoorRock = 0, indoorScissors = 0, indoorPaper = 0, indoorCount = 0;
            int outdoorRock = 0, outdoorScissors = 0, outdoorPaper = 0, outdoorCount = 0;

            for (int i = 0; i < state.PlayerCount; i++)
            {
                Character ch = state.Characters[i];
                if (ch == null || ch.IsDead) continue;

                ChipAllocation alloc = new ChipAllocation(ch.PlayerId);
                alloc.SetRandom(state.Random);

                if (ch.CurrentLocationId == LabLocationId)
                {
                    indoorRock += alloc.Rock;
                    indoorScissors += alloc.Scissors;
                    indoorPaper += alloc.Paper;
                    indoorCount++;
                }
                else
                {
                    outdoorRock += alloc.Rock;
                    outdoorScissors += alloc.Scissors;
                    outdoorPaper += alloc.Paper;
                    outdoorCount++;
                }
            }

            // 双向判定需要两侧都有存活角色
            if (indoorCount == 0 || outdoorCount == 0) return;

            int indoorHand = DominantHand(indoorRock, indoorScissors, indoorPaper, state.Random);
            int outdoorHand = DominantHand(outdoorRock, outdoorScissors, outdoorPaper, state.Random);

            if (!ThresholdRemoved && HandType.Beats(outdoorHand, indoorHand))
                DamageSide(state, insideLab: true);   // 阈值器完好 → 室内输方受伤
            else if (ThresholdRemoved && HandType.Beats(indoorHand, outdoorHand))
                DamageSide(state, insideLab: false);  // 阈值器拆除 → 室外输方受伤
        }

        /// <summary>取筹码最多的手牌；多个并列最高时用确定性随机决定。</summary>
        private static int DominantHand(int rock, int scissors, int paper, DeterministicRandom random)
        {
            int max = rock;
            if (scissors > max) max = scissors;
            if (paper > max) max = paper;

            Span<int> candidates = stackalloc int[3];
            int count = 0;
            if (rock == max) candidates[count++] = HandType.Rock;
            if (scissors == max) candidates[count++] = HandType.Scissors;
            if (paper == max) candidates[count++] = HandType.Paper;

            return count == 1 ? candidates[0] : candidates[random.Next(count)];
        }

        /// <summary>对指定一侧（室内/室外）的所有存活角色施加 1 点法术效果伤。</summary>
        private static void DamageSide(WorldState state, bool insideLab)
        {
            for (int i = 0; i < state.PlayerCount; i++)
            {
                Character ch = state.Characters[i];
                if (ch == null || ch.IsDead) continue;
                if ((ch.CurrentLocationId == LabLocationId) != insideLab) continue;
                DamagePipeline.ApplyDamage(state, -1, ch.Id, 1f, DamageType.Effect);
            }
        }
    }
}
