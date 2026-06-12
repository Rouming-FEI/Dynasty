using System;
using System.Collections.Generic;
using LiteNetLib.Utils;
using Network.Protocol;
using Network.Session;
using Network.Transport;
using NUnit.Framework;

namespace Dynasty.Tests.Network
{
    /// <summary>
    /// Lobby 握手消息级路由（Task 3）：主机 HandleMessage 的 Hello case（版本/满员/角色占用裁决）、
    /// 客户端 JoinAccept/JoinReject 路由、SendHello/RetryHello、传输断线接线
    /// （主机释放槽+冻结+OnPeerLeft、客户端 OnDisconnected）。直调 API 路径（P6 既有测试）不受影响。
    /// </summary>
    public class LobbyHandshakeTests
    {
        private sealed class PeerTransport : INetworkTransport
        {
            public readonly List<(int peer, byte[] data)> Outbound = new List<(int, byte[])>();
            public event Action<int> OnPeerConnected;
            public event Action<int> OnPeerDisconnected;
            public event Action<int, NetDataReader> OnDataReceived;
            public bool IsHost { get; set; }
            public int LocalPeerId { get; set; } = 1;
            public void PollEvents() { }
            public bool SendReliable(int peerId, NetDataWriter w)
            {
                var buf = new byte[w.Length];
                Array.Copy(w.Data, buf, w.Length);
                Outbound.Add((peerId, buf));
                return true;
            }
            public bool Broadcast(NetDataWriter w)
            {
                var buf = new byte[w.Length];
                Array.Copy(w.Data, buf, w.Length);
                Outbound.Add((-1, buf));
                return true;
            }

            /// <summary>测试注入：以 fromPeer 身份把消息直达本端 OnDataReceived（等效真实 PollEvents 派发）。
            /// 事件只能在声明类型内 Invoke（CS0070），故经替身方法转派——与 WindowTests/ReconnectTests
            /// LoopbackTransport 的 DeliverAll/InjectFrom 同型。</summary>
            public void Deliver(int fromPeer, NetDataWriter w)
                => OnDataReceived?.Invoke(fromPeer, new NetDataReader(w.Data));

            public void Deliver(int fromPeer, byte[] data)
                => OnDataReceived?.Invoke(fromPeer, new NetDataReader(data));

            /// <summary>测试注入：模拟对端断线（触发 OnPeerDisconnected）。</summary>
            public void DropPeer(int peerId)
                => OnPeerDisconnected?.Invoke(peerId);
        }

        private static SessionConfig HostConfig(int playerCount)
        {
            var defIds = new int[playerCount];
            defIds[0] = 7;                       // 槽 0 = 主机角色
            for (int i = 1; i < playerCount; i++) defIds[i] = -1;   // AI 槽占位
            return new SessionConfig { PlayerCount = playerCount, RandomSeed = 42, HostPlayerDefIds = defIds };
        }

        private static NetMsgType TypeOf(byte[] data) => (NetMsgType)data[0];
        private static NetDataReader ReaderOf(byte[] data) => new NetDataReader(data);
        private static void SendRaw(PeerTransport t, int fromPeer, NetDataWriter w)
            => t.Deliver(fromPeer, w);
        private static void DeliverBytes(PeerTransport t, int fromPeer, byte[] data)
            => t.Deliver(fromPeer, data);

        [Test]
        public void Hello_成功入座_回JoinAccept_表含真人槽角色()
        {
            var config = HostConfig(4);
            var hostT = new PeerTransport { IsHost = true, LocalPeerId = 0 };
            var hostSession = new NetworkSession(config, true);
            var host = new NetworkGameLoop(hostT, hostSession, config);

            var w = new NetDataWriter();
            NetProtocol.WriteHeader(w, NetMsgType.Hello);
            NetProtocol.WriteHello(w, NetProtocol.ProtocolVersion, 14);   // 请求混沌法师
            SendRaw(hostT, 5, w);

            Assert.That(hostSession.PlayerPeerIds[1], Is.EqualTo(5), "peer 5 入座槽 1");
            Assert.That(hostSession.PlayerDefIds[1], Is.EqualTo(14), "槽 1 写入请求角色");
            Assert.That(hostT.Outbound.Count, Is.EqualTo(1), "主机应回一条消息");
            Assert.That(TypeOf(hostT.Outbound[0].data), Is.EqualTo(NetMsgType.JoinAccept));
            var r = ReaderOf(hostT.Outbound[0].data);
            r.GetByte();
            NetProtocol.ReadJoinAccept(r, out int pid, out int seed, out int[] defs, out _, out _);
            Assert.That(pid, Is.EqualTo(1));
            Assert.That(seed, Is.EqualTo(42));
            Assert.That(defs, Is.EqualTo(new[] { 7, 14, -1, -1 }), "全表：槽 0 主机 + 槽 1 入座 + AI 占位");
        }

        [Test]
        public void Hello_角色被占用_回JoinReject_释放不入座()
        {
            var config = HostConfig(4);
            var hostT = new PeerTransport { IsHost = true, LocalPeerId = 0 };
            var hostSession = new NetworkSession(config, true);
            var host = new NetworkGameLoop(hostT, hostSession, config);
            hostSession.HandleHello(5, 5);
            hostSession.SetPlayerDef(1, 14);   // 槽 1 真人已占 14

            var w = new NetDataWriter();
            NetProtocol.WriteHeader(w, NetMsgType.Hello);
            NetProtocol.WriteHello(w, NetProtocol.ProtocolVersion, 14);   // 重复请求 14
            SendRaw(hostT, 6, w);

            Assert.That(TypeOf(hostT.Outbound[0].data), Is.EqualTo(NetMsgType.JoinReject));
            var r = ReaderOf(hostT.Outbound[0].data);
            r.GetByte();
            Assert.That(r.GetByte(), Is.EqualTo(NetProtocol.JoinRejectReasonRoleTaken));
            Assert.That(hostSession.PlayerPeerIds[2], Is.EqualTo(-1), "拒绝后槽位释放");
            Assert.That(host.IsSlotFrozen(2), Is.False, "占用裁决先于占槽——拒绝不占槽不冻结（fix：不再经 HandleDisconnect 触发冻结）");
        }

        [Test]
        public void Hello_重复peerId请求自持角色_不判占用()
        {
            // fix 回归锁：占用裁决移到占槽前（OwnsRole 排除请求方自己）——已持槽 peer 重复
            // 请求自己已持角色不判占用，仍回 JoinAccept（若排除缺失会把在座玩家误拒/赶出）。
            var config = HostConfig(4);
            var hostT = new PeerTransport { IsHost = true, LocalPeerId = 0 };
            var hostSession = new NetworkSession(config, true);
            var host = new NetworkGameLoop(hostT, hostSession, config);
            hostSession.HandleHello(5, 5);
            hostSession.SetPlayerDef(1, 14);   // peer 5 已持槽 1 角色 14

            var w = new NetDataWriter();
            NetProtocol.WriteHeader(w, NetMsgType.Hello);
            NetProtocol.WriteHello(w, NetProtocol.ProtocolVersion, 14);   // 请求自己已持的 14
            SendRaw(hostT, 5, w);

            Assert.That(TypeOf(hostT.Outbound[hostT.Outbound.Count - 1].data), Is.EqualTo(NetMsgType.JoinAccept), "自持角色不被判占用");
            Assert.That(hostSession.PlayerPeerIds[1], Is.EqualTo(5), "仍持槽 1");
            Assert.That(hostSession.PlayerDefIds[1], Is.EqualTo(14), "角色不变");
            Assert.That(hostSession.PlayerPeerIds[2], Is.EqualTo(-1), "不重复占槽");
        }

        [Test]
        public void Hello_满员_回JoinReject()
        {
            var config = HostConfig(2);   // 2 人局：对端槽仅 1
            var hostT = new PeerTransport { IsHost = true, LocalPeerId = 0 };
            var hostSession = new NetworkSession(config, true);
            var host = new NetworkGameLoop(hostT, hostSession, config);

            var w1 = new NetDataWriter();
            NetProtocol.WriteHeader(w1, NetMsgType.Hello);
            NetProtocol.WriteHello(w1, NetProtocol.ProtocolVersion, 1);
            SendRaw(hostT, 9, w1);
            Assert.That(hostSession.PlayerPeerIds[1], Is.EqualTo(9), "首个对端入座");

            var w2 = new NetDataWriter();
            NetProtocol.WriteHeader(w2, NetMsgType.Hello);
            NetProtocol.WriteHello(w2, NetProtocol.ProtocolVersion, 2);
            SendRaw(hostT, 10, w2);
            Assert.That(TypeOf(hostT.Outbound[hostT.Outbound.Count - 1].data), Is.EqualTo(NetMsgType.JoinReject));
        }

        [Test]
        public void Hello_版本不符_回JoinRejectVersion()
        {
            var config = HostConfig(4);
            var hostT = new PeerTransport { IsHost = true, LocalPeerId = 0 };
            var hostSession = new NetworkSession(config, true);
            var host = new NetworkGameLoop(hostT, hostSession, config);

            var w = new NetDataWriter();
            NetProtocol.WriteHeader(w, NetMsgType.Hello);
            NetProtocol.WriteHello(w, NetProtocol.ProtocolVersion + 1, 1);
            SendRaw(hostT, 5, w);

            Assert.That(TypeOf(hostT.Outbound[0].data), Is.EqualTo(NetMsgType.JoinReject));
            var r = ReaderOf(hostT.Outbound[0].data);
            r.GetByte();
            Assert.That(r.GetByte(), Is.EqualTo(NetProtocol.JoinRejectReasonVersion));
            Assert.That(hostSession.PlayerPeerIds[1], Is.EqualTo(-1), "版本不符不入座");
        }

        [Test]
        public void Hello_defId非法_回JoinRejectInvalidRole_不入座()
        {
            // Task 3 增量（主机值域裁决）：defId <= 0 或目录解析失败 → JoinReject(InvalidRole)，不入座不占槽
            var config = HostConfig(4);
            var hostT = new PeerTransport { IsHost = true, LocalPeerId = 0 };
            var hostSession = new NetworkSession(config, true);
            var host = new NetworkGameLoop(hostT, hostSession, config);

            var w = new NetDataWriter();
            NetProtocol.WriteHeader(w, NetMsgType.Hello);
            NetProtocol.WriteHello(w, NetProtocol.ProtocolVersion, 999);   // 目录中不存在的角色
            SendRaw(hostT, 5, w);

            Assert.That(TypeOf(hostT.Outbound[0].data), Is.EqualTo(NetMsgType.JoinReject));
            var r = ReaderOf(hostT.Outbound[0].data);
            r.GetByte();
            Assert.That(r.GetByte(), Is.EqualTo(NetProtocol.JoinRejectReasonInvalidRole));
            Assert.That(hostSession.PlayerPeerIds[1], Is.EqualTo(-1), "非法角色不入座");
            Assert.That(hostSession.PlayerDefIds[1], Is.EqualTo(-1), "非法角色不占槽");
        }

        [Test]
        public void Hello_重复peerId_拿回原槽_不改角色()
        {
            var config = HostConfig(4);
            var hostT = new PeerTransport { IsHost = true, LocalPeerId = 0 };
            var hostSession = new NetworkSession(config, true);
            var host = new NetworkGameLoop(hostT, hostSession, config);
            hostSession.HandleHello(5, 5);
            hostSession.SetPlayerDef(1, 14);

            var w = new NetDataWriter();
            NetProtocol.WriteHeader(w, NetMsgType.Hello);
            NetProtocol.WriteHello(w, NetProtocol.ProtocolVersion, 3);   // 已持槽 peer 再请求不同角色
            SendRaw(hostT, 5, w);

            Assert.That(hostSession.PlayerPeerIds[1], Is.EqualTo(5), "仍持槽 1");
            Assert.That(hostSession.PlayerPeerIds[2], Is.EqualTo(-1), "不重复占槽");
            Assert.That(hostSession.PlayerDefIds[1], Is.EqualTo(14), "重复 Hello 不改已定角色");
            Assert.That(TypeOf(hostT.Outbound[hostT.Outbound.Count - 1].data), Is.EqualTo(NetMsgType.JoinAccept));
        }

        // ==================== 客户端路由 ====================

        [Test]
        public void 客户端_收JoinAccept_应用入座_触发OnJoined()
        {
            var config = HostConfig(4);
            var clientT = new PeerTransport();
            var clientSession = new NetworkSession(config, false);
            var client = new NetworkGameLoop(clientT, clientSession, config);
            int joined = -1;
            client.OnJoined += pid => joined = pid;

            var w = new NetDataWriter();
            NetProtocol.WriteHeader(w, NetMsgType.JoinAccept);
            NetProtocol.WriteJoinAccept(w, 1, 42, new[] { 7, 14, -1, -1 }, 30000, 20000);
            SendRaw(clientT, 0, w);

            Assert.That(clientSession.Phase, Is.EqualTo(SessionPhase.InGame), "入座后 Phase=InGame（等 GameStart）");
            Assert.That(clientSession.LocalPlayerId, Is.EqualTo(1));
            Assert.That(clientSession.PlayerDefIds, Is.EqualTo(new[] { 7, 14, -1, -1 }));
            Assert.That(joined, Is.EqualTo(1));
        }

        [Test]
        public void 客户端_规模占位_JoinAccept按表修正ConfigPlayerCount()
        {
            // 真实客户端连接前不知房间规模（占位 PlayerCount=8）→ JoinAccept 表长度修正
            var config = HostConfig(4);
            config.PlayerCount = 8;   // 占位规模（客户端视角）
            var clientT = new PeerTransport();
            var clientSession = new NetworkSession(config, false);
            var client = new NetworkGameLoop(clientT, clientSession, config);

            var w = new NetDataWriter();
            NetProtocol.WriteHeader(w, NetMsgType.JoinAccept);
            NetProtocol.WriteJoinAccept(w, 2, 42, new[] { 7, -1, 14, -1 }, 30000, 20000);   // 4 人房
            SendRaw(clientT, 0, w);

            Assert.That(config.PlayerCount, Is.EqualTo(4), "Config.PlayerCount 被表长度修正");
            Assert.That(clientSession.Phase, Is.EqualTo(SessionPhase.InGame));
            Assert.That(clientSession.LocalPlayerId, Is.EqualTo(2));
        }

        [Test]
        public void 客户端_收JoinReject_触发OnJoinRejected()
        {
            var config = HostConfig(4);
            var clientT = new PeerTransport();
            var client = new NetworkGameLoop(clientT, new NetworkSession(config, false), config);
            byte reason = 255;
            client.OnJoinRejected += r => reason = r;

            var w = new NetDataWriter();
            NetProtocol.WriteHeader(w, NetMsgType.JoinReject);
            NetProtocol.WriteJoinReject(w, NetProtocol.JoinRejectReasonRoleTaken);
            SendRaw(clientT, 0, w);

            Assert.That(reason, Is.EqualTo(NetProtocol.JoinRejectReasonRoleTaken));
        }

        [Test]
        public void SendHello_未连接_不发包()
        {
            var config = HostConfig(4);
            var clientT = new PeerTransport { LocalPeerId = -1 };   // 未连接
            var client = new NetworkGameLoop(clientT, new NetworkSession(config, false), config);
            client.RequestedDefId = 14;
            client.SendHello();
            Assert.That(clientT.Outbound.Count, Is.EqualTo(0), "未连接不发 Hello");
        }

        [Test]
        public void SendHello_未设角色_报错不发包()
        {
            var config = HostConfig(4);
            var clientT = new PeerTransport();
            var client = new NetworkGameLoop(clientT, new NetworkSession(config, false), config);
            client.SendHello();   // RequestedDefId 默认 -1
            Assert.That(clientT.Outbound.Count, Is.EqualTo(0));
            Assert.That(client.LastError, Is.Not.Null, "未指定角色应报错");
        }

        [Test]
        public void RetryHello_换角后重发_主机裁决成功()
        {
            var config = HostConfig(4);
            var hostT = new PeerTransport { IsHost = true, LocalPeerId = 0 };
            var hostSession = new NetworkSession(config, true);
            var host = new NetworkGameLoop(hostT, hostSession, config);
            hostSession.HandleHello(5, 5);
            hostSession.SetPlayerDef(1, 14);   // 槽 1 已占 14

            // 第一次请求 14（被占）→ JoinReject
            var clientT = new PeerTransport { LocalPeerId = 6 };
            var client = new NetworkGameLoop(clientT, new NetworkSession(config, false), config);
            client.RequestedDefId = 14;
            client.SendHello();
            byte[] hello1 = clientT.Outbound[0].data;
            clientT.Outbound.Clear();
            DeliverBytes(hostT, 6, hello1);
            Assert.That(hostSession.PlayerPeerIds[2], Is.EqualTo(-1), "占用拒绝未入座");

            client.RequestedDefId = 16;
            client.RetryHello();
            Assert.That(clientT.Outbound.Count, Is.EqualTo(1), "RetryHello 重发");
            DeliverBytes(hostT, 6, clientT.Outbound[0].data);
            Assert.That(hostSession.PlayerPeerIds[2], Is.EqualTo(6), "换角后入座槽 2");
            Assert.That(hostSession.PlayerDefIds[2], Is.EqualTo(16));
        }

        // ==================== 传输断线接线 ====================

        [Test]
        public void 主机_传输断线_释放槽冻结_触发OnPeerLeft()
        {
            var config = HostConfig(4);
            var hostT = new PeerTransport { IsHost = true, LocalPeerId = 0 };
            var hostSession = new NetworkSession(config, true);
            var host = new NetworkGameLoop(hostT, hostSession, config);
            hostSession.HandleHello(5, 5);
            hostSession.SetPlayerDef(1, 14);
            int left = -1;
            host.OnPeerLeft += pid => left = pid;

            hostT.DropPeer(5);

            Assert.That(hostSession.PlayerPeerIds[1], Is.EqualTo(-1), "槽位释放");
            Assert.That(left, Is.EqualTo(1));
            Assert.That(host.IsSlotFrozen(1), Is.True, "断线槽冻结（宽限期等待重连）");
        }

        [Test]
        public void 客户端_传输断线_触发OnDisconnected()
        {
            var config = HostConfig(4);
            var clientT = new PeerTransport();
            var client = new NetworkGameLoop(clientT, new NetworkSession(config, false), config);
            int fired = 0;
            client.OnDisconnected += () => fired++;

            clientT.DropPeer(0);

            Assert.That(fired, Is.EqualTo(1));
        }

        [Test]
        public void 主机_Lobby真人断线_新peer入座空槽_解冻回JoinAccept()
        {
            // 评审遗留 E2E：真人断线（Lobby 期）→ 槽释放 + def 清空 + 冻结 → 新 peer
            // Hello 入座该槽 → OnPlayerJoined 接线解冻 + JoinAccept（断线槽可被新真人接管）。
            var config = HostConfig(4);
            var hostT = new PeerTransport { IsHost = true, LocalPeerId = 0 };
            var hostSession = new NetworkSession(config, true);
            var host = new NetworkGameLoop(hostT, hostSession, config);

            var w1 = new NetDataWriter();
            NetProtocol.WriteHeader(w1, NetMsgType.Hello);
            NetProtocol.WriteHello(w1, NetProtocol.ProtocolVersion, 14);
            SendRaw(hostT, 5, w1);
            Assert.That(hostSession.PlayerPeerIds[1], Is.EqualTo(5), "peer 5 入座槽 1");
            hostT.Outbound.Clear();

            // 真人断线（Lobby 期）：DropPeer → OnPeerDisconnected 接线 → 释放槽 + 清角色 + 冻结
            hostT.DropPeer(5);
            Assert.That(hostSession.PlayerPeerIds[1], Is.EqualTo(-1), "断线槽释放");
            Assert.That(hostSession.PlayerDefIds[1], Is.EqualTo(-1), "Lobby 期角色一并释放（角色可被新玩家选用）");
            Assert.That(host.IsSlotFrozen(1), Is.True, "断线槽冻结（宽限期等待重连）");

            // 新 peer 6 请求同一角色 → 入座空槽 1（OnPlayerJoined 接线解冻）→ 回 JoinAccept
            int joined = -1;
            hostSession.OnPlayerJoined += pid => joined = pid;
            var w2 = new NetDataWriter();
            NetProtocol.WriteHeader(w2, NetMsgType.Hello);
            NetProtocol.WriteHello(w2, NetProtocol.ProtocolVersion, 14);
            SendRaw(hostT, 6, w2);

            Assert.That(hostSession.PlayerPeerIds[1], Is.EqualTo(6), "新 peer 接管原断线槽");
            Assert.That(hostSession.PlayerDefIds[1], Is.EqualTo(14), "新入座者角色写入");
            Assert.That(host.IsSlotFrozen(1), Is.False, "OnPlayerJoined 解冻（槽不再冻结）");
            Assert.That(joined, Is.EqualTo(1), "OnPlayerJoined 触发");
            Assert.That(TypeOf(hostT.Outbound[hostT.Outbound.Count - 1].data), Is.EqualTo(NetMsgType.JoinAccept),
                "新入座者收到 JoinAccept");
        }
    }
}
