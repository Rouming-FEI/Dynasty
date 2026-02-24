using Core.Replay;
using Gameplay.Judgment;
using LiteNetLib.Utils;
using Network.Protocol;
using NUnit.Framework;

namespace Dynasty.Tests.Network
{
    /// <summary>消息协议编解码往返测试（[Type:byte][Payload...] 格式）。</summary>
    public class ProtocolTests
    {
        [Test]
        public void ChipBatch_往返()
        {
            var w = new NetDataWriter();
            NetProtocol.WriteChipBatch(w, 4,
                new ChipAllocation(0) { Rock = 3 },
                new ChipAllocation(1) { Scissors = 2, Paper = 1 },
                new ChipAllocation(2) { Paper = 3 },
                new ChipAllocation(3) { Rock = 1, Scissors = 1, Paper = 1 });
            var r = new NetDataReader(w.Data);
            Assert.That(NetProtocol.ReadType(r), Is.EqualTo(NetMsgType.ChipBatch));
            Assert.That(NetProtocol.ReadChipCount(r), Is.EqualTo(4));
            var a = NetProtocol.ReadChip(r);
            Assert.That(a.PlayerId, Is.EqualTo(0));
            Assert.That(a.Rock, Is.EqualTo(3));
            var b = NetProtocol.ReadChip(r);
            Assert.That(b.Scissors, Is.EqualTo(2));
            Assert.That(b.Paper, Is.EqualTo(1));
        }

        [Test]
        public void ActionRecord_往返()
        {
            var w = new NetDataWriter();
            NetProtocol.WriteHeader(w, NetMsgType.ActionBatch);
            NetProtocol.WriteActionRecord(w, new ReplayActionRecord
            { Tick = 7, PlayerId = 2, Kind = ReplayActionKind.UseSkill, IntArg1 = 1, IntArg2 = 3, FloatArg = 1.5f, Source = 1 });
            NetProtocol.WritePassFlag(w, 0);
            var r = new NetDataReader(w.Data);
            Assert.That(NetProtocol.ReadType(r), Is.EqualTo(NetMsgType.ActionBatch));
            var rec = NetProtocol.ReadActionRecord(r);
            Assert.That(rec.Tick, Is.EqualTo(7));
            Assert.That(rec.PlayerId, Is.EqualTo(2));
            Assert.That(rec.Kind, Is.EqualTo(ReplayActionKind.UseSkill));
            Assert.That(rec.IntArg1, Is.EqualTo(1));
            Assert.That(rec.IntArg2, Is.EqualTo(3));
            Assert.That(rec.FloatArg, Is.EqualTo(1.5f));
            Assert.That(NetProtocol.ReadPassFlag(r), Is.EqualTo(0));
        }

        [Test]
        public void JoinAccept_往返()
        {
            var w = new NetDataWriter();
            NetProtocol.WriteHeader(w, NetMsgType.JoinAccept);
            NetProtocol.WriteJoinAccept(w, 2, 42, new[] { 1, 4, 7, 13 }, 30000, 20000);
            var r = new NetDataReader(w.Data);
            Assert.That(NetProtocol.ReadType(r), Is.EqualTo(NetMsgType.JoinAccept));
            int playerId, seed, judgmentMs, actionMs; int[] defIds;
            NetProtocol.ReadJoinAccept(r, out playerId, out seed, out defIds, out judgmentMs, out actionMs);
            Assert.That(playerId, Is.EqualTo(2));
            Assert.That(seed, Is.EqualTo(42));
            Assert.That(defIds, Is.EqualTo(new[] { 1, 4, 7, 13 }));
        }
    }
}
