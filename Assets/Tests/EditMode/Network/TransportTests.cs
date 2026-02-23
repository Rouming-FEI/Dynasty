using System;
using System.Collections.Generic;
using LiteNetLib.Utils;
using Network.Transport;
using NUnit.Framework;

namespace Dynasty.Tests.Network
{
    /// <summary>真实 UDP 回环测试（127.0.0.1）。端口随机（StartHost(0)）。</summary>
    public class TransportTests
    {
        private sealed class Hub
        {
            public readonly NetworkTransport T = new NetworkTransport();
            public readonly List<int> Connected = new List<int>();
            public readonly List<int> Disconnected = new List<int>();
            public readonly List<(int peer, byte[] data)> Received = new List<(int, byte[])>();

            public Hub()
            {
                T.OnPeerConnected += id => Connected.Add(id);
                T.OnPeerDisconnected += id => Disconnected.Add(id);
                T.OnDataReceived += (id, r) =>
                {
                    // NetPacketReader.Position 起点是包 header 偏移；GetRemainingBytes 直接取全部有效载荷
                    Received.Add((id, r.GetRemainingBytes()));
                };
            }
        }

        /// <summary>泵事件直到双方建立连接（或轮数耗尽）。</summary>
        private static void PumpUntil(Hub a, Hub b, int maxRounds = 200)
        {
            for (int i = 0; i < maxRounds && (a.Connected.Count == 0 || b.Connected.Count == 0); i++)
            {
                a.T.PollEvents();
                b.T.PollEvents();
                System.Threading.Thread.Sleep(5);
            }
        }

        /// <summary>无条件泵固定轮数（收数据/断线包需要后台线程投递后由 PollEvents 派发）。</summary>
        private static void PumpFixed(Hub a, Hub b, int rounds = 300)
        {
            for (int i = 0; i < rounds; i++)
            {
                a.T.PollEvents();
                b.T.PollEvents();
                System.Threading.Thread.Sleep(5);
            }
        }

        [Test]
        public void HostClient_连接收发_回环()
        {
            var host = new Hub();
            var client = new Hub();
            host.T.StartHost(0, 8, "k");
            Assert.That(client.T.Connect("127.0.0.1", host.T.LocalPort, "k", 2000), Is.True);
            PumpUntil(host, client);
            Assert.That(host.Connected.Count, Is.EqualTo(1));
            Assert.That(client.Connected.Count, Is.EqualTo(1));

            var w = new NetDataWriter();
            w.Put("ping");
            Assert.That(client.T.SendReliable(0, w), Is.True);
            PumpFixed(host, client, 300);
            Assert.That(host.Received.Count, Is.EqualTo(1));
            Assert.That(new NetDataReader(host.Received[0].data).GetString(), Is.EqualTo("ping"));

            host.T.DisconnectPeer(0);
            PumpFixed(host, client, 300);
            Assert.That(client.Disconnected.Count, Is.EqualTo(1));
            host.T.Stop();
            client.T.Stop();
        }

        [Test]
        public void Host_组播_全部对端收到()
        {
            var host = new Hub();
            var c1 = new Hub();
            var c2 = new Hub();
            host.T.StartHost(0, 8, "k");
            c1.T.Connect("127.0.0.1", host.T.LocalPort, "k", 2000);
            c2.T.Connect("127.0.0.1", host.T.LocalPort, "k", 2000);
            PumpUntil(host, c1);
            PumpUntil(host, c2);
            Assert.That(host.Connected.Count, Is.EqualTo(2));

            var w = new NetDataWriter();
            w.Put(12345);
            host.T.Broadcast(w);
            PumpFixed(c1, c2, 300);
            PumpFixed(host, c1, 300);
            Assert.That(c1.Received.Count, Is.EqualTo(1));
            Assert.That(c2.Received.Count, Is.EqualTo(1));
            Assert.That(new NetDataReader(c1.Received[0].data).GetInt(), Is.EqualTo(12345));
            Assert.That(new NetDataReader(c2.Received[0].data).GetInt(), Is.EqualTo(12345));
            host.T.Stop();
            c1.T.Stop();
            c2.T.Stop();
        }

        [Test]
        public void 错误连接Key_被拒()
        {
            var host = new Hub();
            var client = new Hub();
            host.T.StartHost(0, 8, "right");
            Assert.That(client.T.Connect("127.0.0.1", host.T.LocalPort, "wrong", 500), Is.True);
            PumpUntil(host, client, 100);
            Assert.That(host.Connected.Count, Is.EqualTo(0), "错误 key 不应建立连接");
            host.T.Stop();
            client.T.Stop();
        }
    }
}
