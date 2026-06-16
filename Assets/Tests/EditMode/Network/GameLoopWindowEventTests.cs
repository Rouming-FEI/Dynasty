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
    /// 另含 I-1 裁决回归：开战解冻 Lobby 冻结槽（断线弃权玩家不阻塞开局判定窗口）。
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

        // ==================== I-1 裁决回归：开战解冻 Lobby 冻结槽 ====================

        /// <summary>Lobby 期断线未归玩家弃权：开战即房间定稿，冻结槽解除冻结并参与 AI 补位，
        /// 判定窗口不按真人挂起。用真实消息/会话路径（传输断线接线 → HandleDisconnect → 冻结）。</summary>
        [Test]
        public void 开战解冻Lobby冻结槽_弃权槽AI补位_判定窗口不挂起()
        {
            var config = new SessionConfig
            {
                PlayerCount = 2, RandomSeed = 42,
                JudgmentWindowMs = 60000, ActionWindowMs = 60000,
                ReconnectGraceMs = 60000,   // 宽限期远长于测试运行时间：排除「宽限到自动回落 AI」掩盖缺陷
                HostPlayerDefIds = new[] { 1, 2 }
            };
            var hostT = new BounceTransport(0);
            var hostSession = new NetworkSession(config, true);
            hostSession.HandleHello(1, 1);   // 槽 1 真人（对端 peer 1）
            var host = new NetworkGameLoop(hostT, hostSession, config);

            // Lobby 期对端断线（真实传输断线接线）：槽释放 + 角色一并释放（Lobby 语义）+ 冻结
            hostT.DropPeer(1);
            Assert.That(hostSession.PlayerPeerIds[1], Is.EqualTo(-1), "断线槽释放");
            Assert.That(hostSession.PlayerDefIds[1], Is.EqualTo(-1), "Lobby 断线角色一并释放");
            Assert.That(host.IsSlotFrozen(1), Is.True, "断线槽冻结（宽限期等待重连）");

            // 主机开战：TryStartGame + StartGame → 开战解冻弃权槽 → 槽 1 参与 AI 确定性补位
            Assert.That(hostSession.TryStartGame(), Is.True);
            host.StartGame();
            Assert.That(host.State, Is.Not.Null, "开局成功");
            Assert.That(host.IsSlotFrozen(1), Is.False, "开战即定稿：Lobby 断线未归弃权，冻结解除");
            Assert.That(hostSession.PlayerDefIds[1], Is.Not.EqualTo(-1), "弃权槽角色参与 AI 补位");

            // 判定窗口能推进（不再按真人挂起）：泵几轮 Tick + 槽 0 提交筹码 → 判定结算收口
            int jEvents = 0;
            host.OnJudgmentWindow += (t, ms) => jEvents++;
            var c0 = new ChipAllocation(0); c0.Set(3, 0, 0);
            int guard = 0;
            while (host.State.Phase == TurnPhase.JudgmentPhase && guard++ < 300)
            {
                host.Tick();
                if (host.State.Phase == TurnPhase.JudgmentPhase)
                    host.SubmitChip(0, c0);
            }
            Assert.That(host.State.Phase, Is.EqualTo(TurnPhase.ActionPhase), "弃权槽解冻补 AI 后判定结算收口，窗口不再挂起");
            Assert.That(jEvents, Is.GreaterThanOrEqualTo(1), "判定窗口事件出现");
        }
    }
}
