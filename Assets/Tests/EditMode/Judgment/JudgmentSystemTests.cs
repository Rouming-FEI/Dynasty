using System;
using NUnit.Framework;
using Core;
using Gameplay.Judgment;

namespace Dynasty.Tests.Judgment
{
    public class JudgmentSystemTests
    {
        private JudgmentSystem _judge;
        private DeterministicRandom _rng;

        [SetUp]
        public void SetUp()
        {
            _judge = new JudgmentSystem();
            _rng = new DeterministicRandom(42);
        }

        private static ChipAllocation Chips(int playerId, int rock, int scissors, int paper)
        {
            var a = new ChipAllocation(playerId);
            a.Set(rock, scissors, paper);
            return a;
        }

        // -- 胜手/败手推导 --

        [Test]
        public void 唯一最高手为胜手_被克制者为败手()
        {
            // 石3 剪0 布0 vs 石0 剪3 布0：石头总3 剪刀总3 布0
            // 石=剪 双高 + 布最低 → 克制布(2) 者 = 石头(0) 为胜手，布为败手
            Span<ChipAllocation> alloc = stackalloc ChipAllocation[2];
            alloc[0] = Chips(0, 3, 0, 0);
            alloc[1] = Chips(1, 0, 3, 0);
            int flat = 0;

            JudgmentResult r = _judge.Resolve(alloc, _rng, ref flat);

            Assert.AreEqual(HandType.Rock, r.WinnerHand);
            Assert.AreEqual(HandType.Paper, r.LoserHand);
            Assert.AreEqual(HandType.Scissors, r.NeutralHand);
        }

        [Test]
        public void 败手是胜手克制的那方_不是克制胜手的那方()
        {
            // 回归：曾因误用 BeatenBy 导致败手推导反向（fix 6f1e7cf）
            // 石4 剪1 布0：石头唯一最高 → 石头胜手，剪刀败手
            Span<ChipAllocation> alloc = stackalloc ChipAllocation[3];
            alloc[0] = Chips(0, 2, 0, 0);
            alloc[1] = Chips(1, 2, 0, 0);
            alloc[2] = Chips(2, 0, 1, 0);
            int flat = 0;

            JudgmentResult r = _judge.Resolve(alloc, _rng, ref flat);

            Assert.AreEqual(HandType.Rock, r.WinnerHand);
            Assert.AreEqual(HandType.Scissors, r.LoserHand); // 石头砸剪刀
            Assert.AreEqual(HandType.Paper, r.NeutralHand);
        }

        // -- 角色判定 --

        [Test]
        public void 胜手筹码多于败手筹码_判为胜者()
        {
            Span<ChipAllocation> alloc = stackalloc ChipAllocation[2];
            alloc[0] = Chips(0, 2, 0, 1); // 石2(胜手) 布1 → 胜
            alloc[1] = Chips(1, 1, 1, 1); // 1-1-1 → 中立
            int flat = 0;

            JudgmentResult r = _judge.Resolve(alloc, _rng, ref flat);

            Assert.AreEqual(JudgmentRole.Winner, r.Roles[0]);
            Assert.AreEqual(2, r.EffectiveChips[0]);
            Assert.AreEqual(JudgmentRole.Neutral, r.Roles[1]);
        }

        [Test]
        public void 败手筹码多于胜手筹码_判为败者()
        {
            // 石3 剪2：石头唯一最高（3>2>0）→ 石头胜手，剪刀败手（BeatsWho）
            Span<ChipAllocation> alloc = stackalloc ChipAllocation[2];
            alloc[0] = Chips(0, 3, 0, 0); // 石3(胜手) → 胜者
            alloc[1] = Chips(1, 0, 2, 0); // 剪2(败手) → 败者
            int flat = 0;

            JudgmentResult r = _judge.Resolve(alloc, _rng, ref flat);

            Assert.AreEqual(JudgmentRole.Loser, r.Roles[1]);
            Assert.AreEqual(2, r.EffectiveChips[1]);
        }

        // -- AP 分配（新规则：胜者保底 1 AP） --

        [Test]
        public void 有败者时_胜者AP等于败者人数()
        {
            // 石3 剪1 布0：石头唯一最高胜手，剪刀败手
            Span<ChipAllocation> alloc = stackalloc ChipAllocation[3];
            alloc[0] = Chips(0, 3, 0, 0); // 胜者
            alloc[1] = Chips(1, 0, 1, 0); // 败者
            alloc[2] = Chips(2, 1, 1, 1); // 中立
            int flat = 0;

            JudgmentResult r = _judge.Resolve(alloc, _rng, ref flat);

            Assert.AreEqual(1, r.LoserCount);
            Assert.AreEqual(1, r.APGained[0]); // 胜者 AP = 败者人数 = 1
            Assert.AreEqual(0, r.APGained[1]);
            Assert.AreEqual(0, r.APGained[2]);
        }

        [Test]
        public void 无败者时_胜者保底1AP()
        {
            // 全押石头（胜手）：石头总6 剪0 布0 → 石头唯一最高胜手，剪刀败手
            // 但无人押剪刀 → 无败者 → 胜者保底 1 AP
            Span<ChipAllocation> alloc = stackalloc ChipAllocation[2];
            alloc[0] = Chips(0, 3, 0, 0);
            alloc[1] = Chips(1, 3, 0, 0);
            int flat = 0;

            JudgmentResult r = _judge.Resolve(alloc, _rng, ref flat);

            Assert.AreEqual(0, r.LoserCount);
            Assert.AreEqual(1, r.APGained[0]); // 保底
            Assert.AreEqual(1, r.APGained[1]);
        }

        [Test]
        public void 胜者AP上限为玩家人数减一()
        {
            // 3 人局：石3 剪1 剪1 → 石头唯一最高，2 个败者 → 胜者 AP = min(2, 2) = 2
            Span<ChipAllocation> alloc = stackalloc ChipAllocation[3];
            alloc[0] = Chips(0, 3, 0, 0); // 胜者
            alloc[1] = Chips(1, 0, 1, 0); // 败者
            alloc[2] = Chips(2, 0, 1, 0); // 败者
            int flat = 0;

            JudgmentResult r = _judge.Resolve(alloc, _rng, ref flat);

            Assert.AreEqual(2, r.LoserCount);
            Assert.AreEqual(2, r.APGained[0]);
        }

        // -- 全平筹码 --

        [Test]
        public void 第一次全平_作废且无人得AP()
        {
            Span<ChipAllocation> alloc = stackalloc ChipAllocation[3];
            alloc[0] = Chips(0, 1, 1, 1);
            alloc[1] = Chips(1, 1, 1, 1);
            alloc[2] = Chips(2, 1, 1, 1);
            int flat = 0;

            JudgmentResult r = _judge.Resolve(alloc, _rng, ref flat);

            Assert.IsTrue(r.IsGlobalFlat);
            Assert.AreEqual(1, flat);
            Assert.AreEqual(0, r.APGained[0]);
            Assert.AreEqual(0, r.APGained[1]);
            Assert.AreEqual(0, r.APGained[2]);
        }

        [Test]
        public void 连续两次全平_全员得1AP且计数归零()
        {
            Span<ChipAllocation> alloc = stackalloc ChipAllocation[3];
            alloc[0] = Chips(0, 1, 1, 1);
            alloc[1] = Chips(1, 1, 1, 1);
            alloc[2] = Chips(2, 1, 1, 1);
            int flat = 1; // 上一轮已平一次

            JudgmentResult r = _judge.Resolve(alloc, _rng, ref flat);

            Assert.IsTrue(r.IsGlobalFlat);
            Assert.AreEqual(0, flat, "保底发放后计数应归零");
            Assert.AreEqual(1, r.APGained[0]);
            Assert.AreEqual(1, r.APGained[1]);
            Assert.AreEqual(1, r.APGained[2]);
        }

        // -- 确定性 --

        [Test]
        public void 相同输入两次判定结果一致()
        {
            Span<ChipAllocation> a1 = stackalloc ChipAllocation[2];
            a1[0] = Chips(0, 2, 1, 0); a1[1] = Chips(1, 0, 1, 2);
            Span<ChipAllocation> a2 = stackalloc ChipAllocation[2];
            a2[0] = Chips(0, 2, 1, 0); a2[1] = Chips(1, 0, 1, 2);

            int f1 = 0, f2 = 0;
            JudgmentResult r1 = new JudgmentSystem().Resolve(a1, new DeterministicRandom(7), ref f1);
            JudgmentResult r2 = new JudgmentSystem().Resolve(a2, new DeterministicRandom(7), ref f2);

            Assert.AreEqual(r1.WinnerHand, r2.WinnerHand);
            Assert.AreEqual(r1.LoserHand, r2.LoserHand);
            Assert.AreEqual(r1.Roles[0], r2.Roles[0]);
            Assert.AreEqual(r1.APGained[0], r2.APGained[0]);
        }
    }
}
