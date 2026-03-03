using System.IO;
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
            NetProtocol.WriteHeader(w, NetMsgType.ChipBatch);
            NetProtocol.WriteChipBatch(w, 3, new[]
            {
                new ChipAllocation(0) { Rock = 3 },
                new ChipAllocation(1) { Scissors = 2, Paper = 1 },
                new ChipAllocation(2) { Paper = 3 },
                new ChipAllocation(3) { Rock = 1, Scissors = 1, Paper = 1 }
            });
            var r = new NetDataReader(w.Data);
            Assert.That(NetProtocol.ReadType(r), Is.EqualTo(NetMsgType.ChipBatch));
            Assert.That(NetProtocol.ReadChipTurn(r), Is.EqualTo(3));
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
            { Tick = 7, PlayerId = 2, Kind = ReplayActionKind.UseSkill, IntArg1 = 1, IntArg2 = 3, IntArg3 = 5, FloatArg = 1.5f, Source = 1 });
            NetProtocol.WritePassFlag(w, 0);
            var r = new NetDataReader(w.Data);
            Assert.That(NetProtocol.ReadType(r), Is.EqualTo(NetMsgType.ActionBatch));
            var rec = NetProtocol.ReadActionRecord(r);
            Assert.That(rec.Tick, Is.EqualTo(7));
            Assert.That(rec.PlayerId, Is.EqualTo(2));
            Assert.That(rec.Kind, Is.EqualTo(ReplayActionKind.UseSkill));
            Assert.That(rec.IntArg1, Is.EqualTo(1));
            Assert.That(rec.IntArg2, Is.EqualTo(3));
            Assert.That(rec.IntArg3, Is.EqualTo(5));
            Assert.That(rec.FloatArg, Is.EqualTo(1.5f));
            Assert.That(rec.Source, Is.EqualTo(1));
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
            Assert.That(judgmentMs, Is.EqualTo(30000));
            Assert.That(actionMs, Is.EqualTo(20000));
        }

        [Test]
        public void ChipBatch_批量读_往返()
        {
            var w = new NetDataWriter();
            NetProtocol.WriteHeader(w, NetMsgType.ChipBatch);
            NetProtocol.WriteChipBatch(w, 5, new[]
            {
                new ChipAllocation(0) { Rock = 2, Paper = 1 },
                new ChipAllocation(1) { Scissors = 3 }
            });
            var r = new NetDataReader(w.Data);
            Assert.That(NetProtocol.ReadType(r), Is.EqualTo(NetMsgType.ChipBatch));
            int turn; ChipAllocation[] batch;
            NetProtocol.ReadChipBatch(r, out turn, out batch);
            Assert.That(turn, Is.EqualTo(5));
            Assert.That(batch.Length, Is.EqualTo(2));
            Assert.That(batch[0].Rock, Is.EqualTo(2));
            Assert.That(batch[0].Paper, Is.EqualTo(1));
            Assert.That(batch[1].Scissors, Is.EqualTo(3));
        }

        [Test]
        public void RoundStart_往返()
        {
            var w = new NetDataWriter();
            NetProtocol.WriteHeader(w, NetMsgType.RoundStart);
            NetProtocol.WriteRoundStart(w, 3, 30000);
            var r = new NetDataReader(w.Data);
            Assert.That(NetProtocol.ReadType(r), Is.EqualTo(NetMsgType.RoundStart));
            int turn, windowMs;
            NetProtocol.ReadRoundStart(r, out turn, out windowMs);
            Assert.That(turn, Is.EqualTo(3));
            Assert.That(windowMs, Is.EqualTo(30000));
        }

        [Test]
        public void ActionTurn_往返()
        {
            var w = new NetDataWriter();
            NetProtocol.WriteHeader(w, NetMsgType.ActionTurn);
            NetProtocol.WriteActionTurn(w, 1, 20000);
            var r = new NetDataReader(w.Data);
            Assert.That(NetProtocol.ReadType(r), Is.EqualTo(NetMsgType.ActionTurn));
            int playerId, windowMs;
            NetProtocol.ReadActionTurn(r, out playerId, out windowMs);
            Assert.That(playerId, Is.EqualTo(1));
            Assert.That(windowMs, Is.EqualTo(20000));
        }

        [Test]
        public void GameOver_往返()
        {
            var w = new NetDataWriter();
            NetProtocol.WriteHeader(w, NetMsgType.GameOver);
            NetProtocol.WriteGameOver(w, 2);
            var r = new NetDataReader(w.Data);
            Assert.That(NetProtocol.ReadType(r), Is.EqualTo(NetMsgType.GameOver));
            Assert.That(NetProtocol.ReadGameOver(r), Is.EqualTo(2));
        }

        [Test]
        public void Snapshot_往返()
        {
            byte[] payload = { 1, 2, 3, 250 };
            var w = new NetDataWriter();
            NetProtocol.WriteHeader(w, NetMsgType.Snapshot);
            NetProtocol.WriteSnapshot(w, 42, payload, payload.Length);
            var r = new NetDataReader(w.Data);
            Assert.That(NetProtocol.ReadType(r), Is.EqualTo(NetMsgType.Snapshot));
            int tick; byte[] bytes;
            NetProtocol.ReadSnapshot(r, out tick, out bytes);
            Assert.That(tick, Is.EqualTo(42));
            Assert.That(bytes, Is.EqualTo(payload));
        }

        [Test]
        public void ResyncReply_往返()
        {
            byte[] snap = { 1, 2, 3, 250 };
            byte[] logs = { 9, 8, 7 };
            var w = new NetDataWriter();
            NetProtocol.WriteHeader(w, NetMsgType.ResyncReply);
            NetProtocol.WriteResyncReply(w, 88, snap, snap.Length, logs, logs.Length);
            var r = new NetDataReader(w.Data);
            Assert.That(NetProtocol.ReadType(r), Is.EqualTo(NetMsgType.ResyncReply));
            int snapshotTick; byte[] snapshotBytes; int logCount; byte[] logBytes;
            NetProtocol.ReadResyncReply(r, out snapshotTick, out snapshotBytes, out logCount, out logBytes);
            Assert.That(snapshotTick, Is.EqualTo(88));
            Assert.That(snapshotBytes, Is.EqualTo(snap));
            Assert.That(logCount, Is.EqualTo(logs.Length));
            Assert.That(logBytes, Is.EqualTo(logs));
        }

        [Test]
        public void JoinAccept_超长defIds_拒绝()
        {
            var w = new NetDataWriter();
            NetProtocol.WriteHeader(w, NetMsgType.JoinAccept);
            w.Put(0); w.Put(0); w.Put(int.MaxValue); // defIds count 超上限
            var r = new NetDataReader(w.Data);
            NetProtocol.ReadType(r);
            Assert.Throws<InvalidDataException>(() => NetProtocol.ReadJoinAccept(r, out _, out _, out _, out _, out _));
        }

        [Test]
        public void JoinAccept_负数defIds_拒绝()
        {
            var w = new NetDataWriter();
            NetProtocol.WriteHeader(w, NetMsgType.JoinAccept);
            w.Put(0); w.Put(0); w.Put(-1); // defIds count 为负
            var r = new NetDataReader(w.Data);
            NetProtocol.ReadType(r);
            Assert.Throws<InvalidDataException>(() => NetProtocol.ReadJoinAccept(r, out _, out _, out _, out _, out _));
        }

        [Test]
        public void 未知类型_兜底为Ping()
        {
            var w = new NetDataWriter();
            w.Put((byte)200); // 未知头字节
            var r = new NetDataReader(w.Data);
            Assert.That(NetProtocol.ReadType(r), Is.EqualTo(NetMsgType.Ping));
        }
    }
}
