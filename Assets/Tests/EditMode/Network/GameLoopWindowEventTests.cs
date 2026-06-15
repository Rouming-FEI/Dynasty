using System;
using System.Collections.Generic;
using Core;
using Gameplay.Judgment;
using LiteNetLib.Utils;
using Network.Protocol;
using Network.Session;
using Network.Transport;
using NUnit.Framework;

namespace Dynasty.Tests.Network
{
    /// <summary>
    /// Task 5：窗口事件出口——主机 OpenWindow/OpenActionWindow 本地触发、客户端 RoundStart/
    /// ActionTurn 广播到达触发 OnJudgmentWindow/OnActionWindow（UI 交互对齐信号）。
    /// </summary>
    public class GameLoopWindowEventTests
    {
        private sealed class BounceTransport : INetworkTransport
        {
            public BounceTransport Peer;
            public readonly int SelfPeerId;
            public readonly List<(int peer, byte[] data)> Outbound = new List<(int, byte[])>();
            public event Action<int> OnPeerConnected;
            public event Action<int> OnPeerDisconnected;
            public event Action<int, NetDataReader> OnDataReceived;
            public BounceTransport(int selfPeerId) { SelfPeerId = selfPeerId; }
            public bool IsHost => false;
            public int LocalPeerId => SelfPeerId;
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
                Outbound.Add((0, buf));
                return true;
            }
            public void DeliverAll()
            {
                var batch = Outbound.ToArray();
                Outbound.Clear();
                foreach (var (_, data) in batch)
                    Peer.OnDataReceived?.Invoke(SelfPeerId, new NetDataReader(data));
            }

            /// <summary>测试注入：以指定 peerId 身份把消息直达本端 OnDataReceived（等效真实
            /// PollEvents 派发）。事件只能在声明类型内 Invoke（CS0070），故经替身方法转派。</summary>
            public void Inject(int fromPeer, NetDataWriter w)
            {
                var buf = new byte[w.Length];
                Array.Copy(w.Data, buf, w.Length);
                OnDataReceived?.Invoke(fromPeer, new NetDataReader(buf));
            }

            /// <summary>测试注入：以指定 peerId 触发对端断线事件（传输断线接线路径）。</summary>
            public void DropPeer(int peerId) => OnPeerDisconnected?.Invoke(peerId);
        }

        [Test]
        public void 主机开判定窗_触发OnJudgmentWindow()
        {
            var config = new SessionConfig
            {
                PlayerCount = 2, RandomSeed = 42,
                JudgmentWindowMs = 60000, ActionWindowMs = 60000,
                HostPlayerDefIds = new[] { 1, 2 }
            };
            var hostT = new BounceTransport(0);
            var hostSession = new NetworkSession(config, true);
            hostSession.HandleHello(1, 1);           // 槽 1 真人（对端）
            var host = new NetworkGameLoop(hostT, hostSession, config);
            int events = 0, evTurn = -1, evMs = -1;
            host.OnJudgmentWindow += (t, ms) => { events++; evTurn = t; evMs = ms; };

            host.StartGame();
            int guard = 0;
            while (events == 0 && guard++ < 100) host.Tick();

            Assert.That(events, Is.EqualTo(1), "开判定窗触发一次");
            Assert.That(evTurn, Is.EqualTo(1), "第 1 回合判定");
            Assert.That(evMs, Is.EqualTo(60000));
        }

        [Test]
        public void 客户端_收RoundStart与ActionTurn_触发窗口事件()
        {
            // 纯消息路由：客户端未开局也能消费（载荷即信号）
            var clientT = new BounceTransport(1);
            var clientSession = new NetworkSession(new SessionConfig { PlayerCount = 2 }, false);
            var client = new NetworkGameLoop(clientT, clientSession, new SessionConfig { PlayerCount = 2 });
            int jEvents = 0, jTurn = -1, jMs = -1;
            int aEvents = 0, aPid = -1, aMs = -1;
            client.OnJudgmentWindow += (t, ms) => { jEvents++; jTurn = t; jMs = ms; };
            client.OnActionWindow += (p, ms) => { aEvents++; aPid = p; aMs = ms; };

            var w1 = new NetDataWriter();
            NetProtocol.WriteHeader(w1, NetMsgType.RoundStart);
            NetProtocol.WriteRoundStart(w1, 2, 30000);
            clientT.Inject(0, w1);
            Assert.That(jEvents, Is.EqualTo(1));
            Assert.That(jTurn, Is.EqualTo(2));
            Assert.That(jMs, Is.EqualTo(30000));

            var w2 = new NetDataWriter();
            NetProtocol.WriteHeader(w2, NetMsgType.ActionTurn);
            NetProtocol.WriteActionTurn(w2, 1, 20000);
            clientT.Inject(0, w2);
            Assert.That(aEvents, Is.EqualTo(1));
            Assert.That(aPid, Is.EqualTo(1));
            Assert.That(aMs, Is.EqualTo(20000));
        }

        [Test]
        public void 主机_行动开窗_触发OnActionWindow_轮换到胜者()
        {
            var config = new SessionConfig
            {
                PlayerCount = 2, RandomSeed = 42,
                JudgmentWindowMs = 60000, ActionWindowMs = 60000,
                HostPlayerDefIds = new[] { 1, 2 }
            };
            var hostT = new BounceTransport(0);
            var hostSession = new NetworkSession(config, true);
            hostSession.HandleHello(1, 1);
            var host = new NetworkGameLoop(hostT, hostSession, config);
            int aEvents = 0, aPid = -1;
            host.OnActionWindow += (p, ms) => { aEvents++; aPid = p; };

            host.StartGame();
            // 泵送直至行动开窗（判定结算在提交后下一 Tick 收口，行动窗口再下一 Tick 打开——
            // 泵送写法参考 WindowTests：提交 → 结算 → 开行动窗各需一拍，故以事件为循环终态）
            int guard = 0;
            while (aEvents == 0 && guard++ < 100)
            {
                host.Tick();
                if (host.State != null && host.State.Phase == TurnPhase.JudgmentPhase)
                {
                    // 槽 0 出石头 3、槽 1 出剪刀 3 → 槽 0 唯一胜者先行动
                    var c0 = new ChipAllocation(0); c0.Set(3, 0, 0);
                    var c1 = new ChipAllocation(1); c1.Set(0, 3, 0);
                    host.SubmitChip(0, c0);
                    host.SubmitChip(1, c1);
                }
            }
            Assert.That(aEvents, Is.GreaterThanOrEqualTo(1), "行动开窗触发事件");
            Assert.That(aPid, Is.EqualTo(0), "胜者槽 0 先行动");
        }
    }
}
