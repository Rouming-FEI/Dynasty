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

        [Test]
        public void TwinConvert_记录往返()
        {
            var cmd = new TwinConvertCommand(9, 3, 4); // newItemId=4 施工锤
            ReplayActionRecord r = ReplayActionFactory.ToRecord(cmd);
            Assert.AreEqual(ReplayActionKind.TwinConvert, r.Kind);
            Assert.AreEqual(4, r.IntArg1, "IntArg1 = newItemId");

            ICommand rebuilt = ReplayActionFactory.Create(r, 9);
            Assert.IsInstanceOf<TwinConvertCommand>(rebuilt);
            var rc = (TwinConvertCommand)rebuilt;
            Assert.AreEqual(9, rc.Tick);
            Assert.AreEqual(3, rc.PlayerId);
            Assert.AreEqual(4, rc.NewItemId);
        }
    }
}
