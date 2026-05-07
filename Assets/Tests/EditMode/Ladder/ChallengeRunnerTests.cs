using System;
using Core;
using Core.GAS;
using Gameplay.Characters;
using Gameplay.Ladder;
using NUnit.Framework;

namespace Dynasty.Tests.Ladder
{
    /// <summary>测试 Def 工厂：God 高血量打不死（槽 0 → 必胜路径）；Pawn 1 理智任何伤害即死
    /// （互殴收敛快）。Id 900+ 避开真实角色段（不进 LadderRules.CharacterPool）。</summary>
    public static class ChallengeTestDefs
    {
        public static CharacterDef God() => new CharacterDef
        {
            Id = 900, Name = "测试体·神",
            BaseSanity = 100, BaseErosionLimit = 100,
            BasePhysicalDefense = 0, BaseMagicDefense = 0,
            InitialItem = 1,                          // 刀（近战 1.5，一击秒 Pawn）
            Skills = Array.Empty<SkillDef>()
        };

        public static CharacterDef Pawn() => new CharacterDef
        {
            Id = 901, Name = "测试体·靶",
            BaseSanity = 1, BaseErosionLimit = 1,
            BasePhysicalDefense = 0, BaseMagicDefense = 0,
            InitialItem = 0,
            Skills = Array.Empty<SkillDef>()
        };

        /// <summary>drawDefs：槽 0 = God、其余 = Pawn → 槽 0 每层必胜。</summary>
        public static CharacterDef[] GodVsPawns(int layer, int playerCount, DeterministicRandom rng)
        {
            var defs = new CharacterDef[playerCount];
            defs[0] = God();
            for (int i = 1; i < playerCount; i++) defs[i] = Pawn();
            return defs;
        }

        /// <summary>drawDefs：槽 0 = Pawn、其余 = God → 槽 0 必败（失败路径）。</summary>
        public static CharacterDef[] PawnVsGods(int layer, int playerCount, DeterministicRandom rng)
        {
            var defs = new CharacterDef[playerCount];
            defs[0] = Pawn();
            for (int i = 1; i < playerCount; i++) defs[i] = God();
            return defs;
        }

        /// <summary>drawDefs：全员 God → 一回合内不可能终局（护栏分支测试）。</summary>
        public static CharacterDef[] AllGods(int layer, int playerCount, DeterministicRandom rng)
        {
            var defs = new CharacterDef[playerCount];
            for (int i = 0; i < playerCount; i++) defs[i] = God();
            return defs;
        }
    }

    public class ChallengeRunnerTests
    {
        [Test]
        public void GodVsPawns_TwoLayerTower_ClimbsAndWins()
        {
            var store = new InMemoryProfileStore();
            var cfg = new ChallengeConfig { Seed = 12345, LayerCap = 2 };
            var runner = new ChallengeRunner(cfg, store, ChallengeTestDefs.GodVsPawns);
            ChallengeResult r = runner.Run();

            Assert.IsTrue(r.Won);
            Assert.AreEqual(2, r.Streak);
            Assert.AreEqual(0, r.FailedAtLayer);
            Assert.IsFalse(r.TimedOut);
            Assert.AreEqual(30L, r.Score);
            LadderProfile p = store.Load();
            Assert.AreEqual(2, p.BestStreak);
            Assert.AreEqual(1, p.Attempts);
        }

        [Test]
        public void PawnVsGods_FirstLayerDeath_RecordsZeroStreak()
        {
            var store = new InMemoryProfileStore();
            var cfg = new ChallengeConfig { Seed = 999, LayerCap = 3 };
            var runner = new ChallengeRunner(cfg, store, ChallengeTestDefs.PawnVsGods);
            ChallengeResult r = runner.Run();

            Assert.IsFalse(r.Won);
            Assert.AreEqual(0, r.Streak);
            Assert.AreEqual(0L, r.Score);
            Assert.AreEqual(1, r.FailedAtLayer);
            Assert.IsFalse(r.TimedOut);
            Assert.AreEqual(0, store.Load().BestStreak);
            Assert.AreEqual(1, store.Load().Attempts);
        }

        [Test]
        public void AllGods_SingleRoundCap_TimesOutAsFailure()
        {
            var store = new InMemoryProfileStore();
            var cfg = new ChallengeConfig { Seed = 42, LayerCap = 3, MaxRoundsPerLayer = 1 };
            var runner = new ChallengeRunner(cfg, store, ChallengeTestDefs.AllGods);
            ChallengeResult r = runner.Run();

            Assert.IsFalse(r.Won);
            Assert.AreEqual(0, r.Streak);
            Assert.AreEqual(1, r.FailedAtLayer);
            Assert.IsTrue(r.TimedOut);
            Assert.AreEqual(1, store.Load().Attempts);
        }

        [Test]
        public void SameSeed_DefaultPool_TwoRunsIdentical()
        {
            var cfg = new ChallengeConfig { Seed = 777, LayerCap = 3 };
            var s1 = new InMemoryProfileStore();
            var s2 = new InMemoryProfileStore();
            ChallengeResult a = new ChallengeRunner(cfg, s1).Run();
            ChallengeResult b = new ChallengeRunner(cfg, s2).Run();

            Assert.AreEqual(a.Streak, b.Streak);
            Assert.AreEqual(a.Score, b.Score);
            Assert.AreEqual(a.Won, b.Won);
            Assert.AreEqual(a.FailedAtLayer, b.FailedAtLayer);
            Assert.AreEqual(a.TimedOut, b.TimedOut);
            Assert.AreEqual(s1.Load().TotalScore, s2.Load().TotalScore);
            Assert.AreEqual(s1.Load().Attempts, s2.Load().Attempts);
        }

        [Test]
        public void TwoChallenges_AccumulateIntoSharedStore()
        {
            var store = new InMemoryProfileStore();
            var cfgA = new ChallengeConfig { Seed = 111, LayerCap = 2 };
            var cfgB = new ChallengeConfig { Seed = 222, LayerCap = 2 };
            new ChallengeRunner(cfgA, store, ChallengeTestDefs.GodVsPawns).Run();
            ChallengeResult second = new ChallengeRunner(cfgB, store, ChallengeTestDefs.GodVsPawns).Run();

            Assert.AreEqual(2, second.Streak);
            LadderProfile p = store.Load();
            Assert.AreEqual(2, p.BestStreak);
            Assert.AreEqual(60L, p.TotalScore);   // 30 + 30
            Assert.AreEqual(2, p.Attempts);
        }

        [Test]
        public void GodVsPawns_ThenPawn_SecondLayerDeath_RecordsFirstStreak()
        {
            // 层 1 = God 必胜；层 2 起槽 0 = Pawn（必败）→ streak=1、score=10、FailedAtLayer=2
            var store = new InMemoryProfileStore();
            var cfg = new ChallengeConfig { Seed = 31415, LayerCap = 3 };
            CharacterDef[] Draw(int layer, int playerCount, DeterministicRandom rng)
                => layer == 1
                    ? ChallengeTestDefs.GodVsPawns(layer, playerCount, rng)
                    : ChallengeTestDefs.PawnVsGods(layer, playerCount, rng);
            var runner = new ChallengeRunner(cfg, store, Draw);
            ChallengeResult r = runner.Run();

            Assert.IsFalse(r.Won);
            Assert.AreEqual(1, r.Streak);
            Assert.AreEqual(10L, r.Score);
            Assert.AreEqual(2, r.FailedAtLayer);
            Assert.IsFalse(r.TimedOut);
            Assert.AreEqual(1, store.Load().BestStreak);
            Assert.AreEqual(1, store.Load().Attempts);
        }

        [Test]
        public void LayerCapBeyondMax_Throws()
        {
            var store = new InMemoryProfileStore();
            var cfg = new ChallengeConfig { Seed = 1, LayerCap = LadderRules.MaxLayers + 1 };
            var runner = new ChallengeRunner(cfg, store, ChallengeTestDefs.AllGods);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => runner.Run());
        }
    }
}
