using System.IO;
using Gameplay.Characters;
using LiteNetLib.Utils;
using Network.Protocol;
using Network.Session;
using Network.Transport;
using NUnit.Framework;

namespace Dynasty.Tests.Network
{
    /// <summary>本文件消息路由用替身：SendReliable/Broadcast 入 Outbound；Inject 直达 OnDataReceived。</summary>
    internal sealed class TransportStub : INetworkTransport
    {
        public readonly System.Collections.Generic.List<(int peer, byte[] data)> Outbound
            = new System.Collections.Generic.List<(int, byte[])>();
        public event System.Action<int> OnPeerConnected;
        public event System.Action<int> OnPeerDisconnected;
        public event System.Action<int, NetDataReader> OnDataReceived;
        public bool IsHost => false;
        public int LocalPeerId { get; set; } = 0;
        public void PollEvents() { }
        public bool SendReliable(int peerId, NetDataWriter w)
        {
            var buf = new byte[w.Length];
            System.Array.Copy(w.Data, buf, w.Length);
            Outbound.Add((peerId, buf));
            return true;
        }
        public bool Broadcast(NetDataWriter w)
        {
            var buf = new byte[w.Length];
            System.Array.Copy(w.Data, buf, w.Length);
            Outbound.Add((-1, buf));
            return true;
        }
        public void Inject(int peerId, NetDataWriter w)
        {
            var buf = new byte[w.Length];
            System.Array.Copy(w.Data, buf, w.Length);
            OnDataReceived?.Invoke(peerId, new NetDataReader(buf));
        }
    }

    /// <summary>GameStart 载荷扩展（最终角色表广播）与 CharacterDefCatalog.AllIds 全集。</summary>
    public class GameStartDefsTests
    {
        [Test]
        public void AllIds_覆盖16角色全集()
        {
            Assert.That(CharacterDefCatalog.AllIds, Is.Not.Null);
            Assert.That(CharacterDefCatalog.AllIds.Length, Is.EqualTo(16), "16 角色全集");
            for (int i = 1; i <= 16; i++)
            {
                Assert.That(CharacterDefCatalog.AllIds, Does.Contain(i), $"缺角色 {i}");
                Assert.That(CharacterDefCatalog.Get(i), Is.Not.Null, $"角色 {i} 无法解析 Def");
            }
        }

        [Test]
        public void GameStart_载荷往返_含全表()
        {
            var w = new NetDataWriter();
            NetProtocol.WriteHeader(w, NetMsgType.GameStart);
            NetProtocol.WriteGameStart(w, 3, new[] { 1, 14, -1, 16 });

            var r = new NetDataReader(w.Data);
            Assert.That(NetProtocol.ReadType(r), Is.EqualTo(NetMsgType.GameStart));
            NetProtocol.ReadGameStart(r, out int turn, out int[] defIds);
            Assert.That(turn, Is.EqualTo(3));
            Assert.That(defIds, Is.EqualTo(new[] { 1, 14, -1, 16 }));
        }

        [Test]
        public void GameStart_超长数组_拒绝()
        {
            // 载荷 = [turn:int][count:int][defIds:int×count]——伪造值必须直接落在 count 字段
            // （WriteGameStart 空表会先写 count=0，尾部追加只能进 defIds 数据区，触发不了防护；
            // 写法对齐 ProtocolTests.JoinAccept_超长defIds_拒绝 负例）
            var w = new NetDataWriter();
            NetProtocol.WriteHeader(w, NetMsgType.GameStart);
            w.Put(1);                   // turn 合法
            w.Put(int.MaxValue / 2);    // 伪造超长表长度 → CheckLength 拒绝
            var r = new NetDataReader(w.Data);
            NetProtocol.ReadType(r);
            Assert.Throws<InvalidDataException>(() => NetProtocol.ReadGameStart(r, out _, out _));
        }

        [Test]
        public void JoinReject_原因常量_稳定()
        {
            Assert.That(NetProtocol.JoinRejectReasonFull, Is.EqualTo(0));
            Assert.That(NetProtocol.JoinRejectReasonVersion, Is.EqualTo(1));
            Assert.That(NetProtocol.JoinRejectReasonRoleTaken, Is.EqualTo(2));
        }

        // ==================== Task 4：AI 槽补位 + GameStart 全表应用 ====================

        [Test]
        public void StartGame_AI槽补位_确定性_不重复真人角色()
        {
            var config = AiSlotConfig(4);
            var session = new NetworkSession(config, true);
            var loop = new NetworkGameLoop(new TransportStub(), session, config);
            session.HandleHello(5, 5);
            session.SetPlayerDef(1, 14);   // 真人槽 1 选混沌法师；槽 2/3 保持 -1（AI）

            loop.StartGame();

            int[] defs = session.PlayerDefIds;
            Assert.That(defs[0], Is.EqualTo(7), "槽 0 不变");
            Assert.That(defs[1], Is.EqualTo(14), "真人槽不变");
            Assert.That(defs[2], Is.Not.EqualTo(-1), "AI 槽 2 已补位");
            Assert.That(defs[3], Is.Not.EqualTo(-1), "AI 槽 3 已补位");
            Assert.That(defs[2], Is.Not.EqualTo(7).And.Not.EqualTo(14), "AI 不与真人重复");
            Assert.That(defs[3], Is.Not.EqualTo(7).And.Not.EqualTo(14), "AI 不与真人重复");
            Assert.That(defs[2], Is.Not.EqualTo(defs[3]), "AI 槽互不重复");

            // 确定性：同 seed 双跑补位表一致
            var session2 = new NetworkSession(config, true);
            var loop2 = new NetworkGameLoop(new TransportStub(), session2, config);
            session2.HandleHello(5, 5);
            session2.SetPlayerDef(1, 14);
            loop2.StartGame();
            Assert.That(session2.PlayerDefIds, Is.EqualTo(session.PlayerDefIds), "同 seed 补位表可复现");
        }

        [Test]
        public void StartGame_全真人_无AI槽_表不变()
        {
            var config = AiSlotConfig(2);   // 2 人局全真人：槽 1 真人
            var session = new NetworkSession(config, true);
            var loop = new NetworkGameLoop(new TransportStub(), session, config);
            session.HandleHello(5, 5);
            session.SetPlayerDef(1, 14);
            loop.StartGame();
            Assert.That(session.PlayerDefIds, Is.EqualTo(new[] { 7, 14 }));
        }

        [Test]
        public void 客户端_收GameStart全表_应用后开局()
        {
            var config = AiSlotConfig(4);
            var clientT = new TransportStub();
            var session = new NetworkSession(config, false);
            session.ApplyJoinAccept(1, 42, new[] { 7, 14, -1, -1 });   // 入座时 AI 槽占位
            var client = new NetworkGameLoop(clientT, session, config);
            Assert.That(client.State, Is.Null, "未开局");

            var w = new NetDataWriter();
            NetProtocol.WriteHeader(w, NetMsgType.GameStart);
            NetProtocol.WriteGameStart(w, 1, new[] { 7, 14, 16, 9 });   // 最终表（AI 槽已补）
            clientT.Inject(0, w);

            Assert.That(client.State, Is.Not.Null, "应用最终表后开局");
            Assert.That(session.PlayerDefIds, Is.EqualTo(new[] { 7, 14, 16, 9 }), "会话表被最终表覆盖");
            Assert.That(client.State.PlayerCount, Is.EqualTo(4));
        }

        private static SessionConfig AiSlotConfig(int playerCount)
        {
            var defIds = new int[playerCount];
            defIds[0] = 7;
            for (int i = 1; i < playerCount; i++) defIds[i] = -1;
            return new SessionConfig { PlayerCount = playerCount, RandomSeed = 42, HostPlayerDefIds = defIds };
        }
    }
}
