using Core.Command;
using Core.Replay;
using Gameplay.Characters;
using NUnit.Framework;

namespace Dynasty.Tests.Replay
{
    /// <summary>P7 新命令的 ReplayActionRecord 双向映射：ToRecord → Create → 字段一致。</summary>
    public class P7NewCommandsReplayTests
    {
        [Test]
        public void ChaosSimulate_记录往返()
        {
            var cmd = new ChaosSimulateCommand(5, 2, 3, 1); // itemId=3 枪, target=1
            ReplayActionRecord r = ReplayActionFactory.ToRecord(cmd);
            Assert.AreEqual(ReplayActionKind.ChaosSimulate, r.Kind);
            Assert.AreEqual(3, r.IntArg1, "IntArg1 = itemId");
            Assert.AreEqual(1, r.IntArg2, "IntArg2 = targetPlayerId");

            ICommand rebuilt = ReplayActionFactory.Create(r, 5);
            Assert.IsInstanceOf<ChaosSimulateCommand>(rebuilt);
            var rc = (ChaosSimulateCommand)rebuilt;
            Assert.AreEqual(5, rc.Tick);
            Assert.AreEqual(2, rc.PlayerId);
            Assert.AreEqual(3, rc.ItemId);
            Assert.AreEqual(1, rc.TargetPlayerId);
        }
    }
}
