using Core;
using Gameplay.Characters;
using Gameplay.Ladder;
using NUnit.Framework;

namespace Dynasty.Tests.Ladder
{
    /// <summary>引擎上限 8 人局（第 7 层形态）：护栏内必须收敛到 GameOver，且同参两次确定性一致。
    /// 校准记录：纯 8 Pawn（全无初始武器）互殴无有效伤害源——多数种子槽 0 第 36 回合早死退出、
    /// 其余存活至护栏 400/1000 仍不终局，不可用；改为槽 0=God + 7 Pawn（God 持刀）后
    /// 种子 20260906 第 117 回合收敛（GameOver、槽 0 存活），远在护栏 400 内。</summary>
    public class LayerGameMaxPlayersTests
    {
        private static CharacterDef[] God0Pawns8()
        {
            var defs = new CharacterDef[8];
            defs[0] = ChallengeTestDefs.God();
            for (int i = 1; i < 8; i++) defs[i] = ChallengeTestDefs.Pawn();
            return defs;
        }

        [Test]
        public void EightPlayers_ConvergesWithinCap()
        {
            var info = ChallengeRunner.RunLayerGame(8, 20260906, God0Pawns8(), 400);
            Assert.AreEqual(TurnPhase.GameOver, info.Phase,
                $"8 人局 {info.Rounds} 回合内未收敛（护栏触发）");
        }

        [Test]
        public void EightPlayers_SameSeed_TwoRunsIdentical()
        {
            var a = ChallengeRunner.RunLayerGame(8, 20260906, God0Pawns8(), 400);
            var b = ChallengeRunner.RunLayerGame(8, 20260906, God0Pawns8(), 400);
            Assert.AreEqual(a.Phase, b.Phase);
            Assert.AreEqual(a.Rounds, b.Rounds);
            Assert.AreEqual(a.Slot0Alive, b.Slot0Alive);
        }
    }
}
