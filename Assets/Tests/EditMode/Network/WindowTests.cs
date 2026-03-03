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
    /// 判定窗口测试：用「本地双 Loop 同进程模拟」（内存转发消息，不走真实 UDP），专注窗口状态机——
    /// 开窗等待不推进 / 全部提交→ChipBatch 广播→两端结算一致 / 部分提交+超时→自动 1-1-1 /
    /// 全部超时→全员 1-1-1 引擎保底 / 全局平作废重判→重新开窗。
    /// 真实 UDP 路径由 GameLoopNetTests（Task 12）覆盖。
    /// </summary>
    public class WindowTests
    {
        /// <summary>内存传输替身：SendReliable/Broadcast 写入 Outbound，DeliverAll 转发到对端 OnDataReceived。
        /// 点对点（host↔client），点对点转发不需要真实 UDP。</summary>
        private sealed class LoopbackTransport : INetworkTransport
        {
            public LoopbackTransport Peer;
            public readonly List<(int peer, byte[] data)> Outbound = new List<(int, byte[])>();

            public event Action<int, NetDataReader> OnDataReceived;

            public bool IsHost { get; }

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

            /// <summary>把本端 Outbound 全部转发到对端并清空。</summary>
            public void DeliverAll()
            {
                var batch = Outbound.ToArray();
                Outbound.Clear();
                foreach (var (peer, data) in batch)
                    Peer.OnDataReceived?.Invoke(peer, new NetDataReader(data));
            }
        }

        /// <summary>可手动推进的时钟（测试注入 ClockProvider 控制窗口超时）。</summary>
        private sealed class FakeClock
        {
            public long Now;
        }

        /// <summary>夹具：主机 Loop（槽 0=主机真人 + 1..connectedClients 对端真人 + 其余 AI 槽）
        /// 与客户端 Loop（playerId=1 真人）同进程配对。</summary>
        private sealed class Fixture
        {
            public SessionConfig Config;
            public FakeClock Clock;
            public LoopbackTransport HostTransport;
            public LoopbackTransport ClientTransport;
            public NetworkGameLoop Host;
            public NetworkGameLoop Client;

            public static Fixture Create(int playerCount, int connectedClients, int judgmentMs = 30000, bool startClientDirectly = true)
            {
                var defIds = new int[playerCount];
                for (int i = 0; i < playerCount; i++) defIds[i] = i + 1;
                var config = new SessionConfig
                {
                    PlayerCount = playerCount,
                    RandomSeed = 42,
                    JudgmentWindowMs = judgmentMs,
                    HostPlayerDefIds = defIds
                };

                var hostSession = new NetworkSession(config, true);
                for (int pid = 1; pid <= connectedClients; pid++)
                    Assert.That(hostSession.HandleHello(pid, pid), Is.EqualTo(pid));
                Assert.That(hostSession.TryStartGame(), Is.True);

                var clientSession = new NetworkSession(config, false);
                clientSession.ApplyJoinAccept(1, 42, defIds);

                var ht = new LoopbackTransport();
                var ct = new LoopbackTransport();
                ht.Peer = ct;
                ct.Peer = ht;

                var clock = new FakeClock();
                var host = new NetworkGameLoop(ht, hostSession, config);
                var client = new NetworkGameLoop(ct, clientSession, config);
                host.ClockProvider = () => clock.Now;
                client.ClockProvider = () => clock.Now;
                host.StartGame();   // 主机开局即广播 GameStart
                if (startClientDirectly) client.StartGame();

                return new Fixture
                {
                    Config = config,
                    Clock = clock,
                    HostTransport = ht,
                    ClientTransport = ct,
                    Host = host,
                    Client = client
                };
            }

            /// <summary>一轮泵送：主机 Tick → 主机消息达客户端 → 客户端 Tick → 客户端消息达主机。</summary>
            public void Pump(int rounds = 1)
            {
                for (int r = 0; r < rounds; r++)
                {
                    Host.Tick();
                    HostTransport.DeliverAll();   // 主机 → 客户端
                    Client.Tick();
                    ClientTransport.DeliverAll(); // 客户端 → 主机
                }
            }
        }

        private static ChipAllocation C(int playerId, int rock, int scissors, int paper)
        {
            var a = new ChipAllocation(playerId);
            a.Set(rock, scissors, paper);
            return a;
        }

        [Test]
        public void 窗口开启后Tick不推进_等待提交()
        {
            var f = Fixture.Create(4, 1);
            f.Pump();   // 开窗 + 首次收集（AI 槽缓存，真人槽 0/1 待提交）

            Assert.That(f.Host.State.Phase, Is.EqualTo(TurnPhase.JudgmentPhase));
            Assert.That(f.Client.State.Phase, Is.EqualTo(TurnPhase.JudgmentPhase));
            int tick = f.Host.State.CurrentTick;
            ulong worldRandom = f.Host.State.Random.State;

            f.Pump(3);  // 未提交、未超时 → 窗口保持等待
            Assert.That(f.Host.State.CurrentTick, Is.EqualTo(tick), "等待提交期间判定不得推进 Tick");
            Assert.That(f.Client.State.CurrentTick, Is.EqualTo(tick));
            Assert.That(f.Host.State.Random.State, Is.EqualTo(worldRandom), "等待期间判定未结算，世界随机流不得消耗");
            Assert.That(f.Host.State.Phase, Is.EqualTo(TurnPhase.JudgmentPhase));
            Assert.That(f.Client.State.Phase, Is.EqualTo(TurnPhase.JudgmentPhase));
        }

        [Test]
        public void 全部提交_ChipBatch广播_两端结算一致进入行动阶段()
        {
            var f = Fixture.Create(4, 1);
            f.Pump();   // 开窗
            f.Host.SubmitChip(0, C(0, 3, 0, 0));      // 主机真人槽 0：全石头
            f.Client.SubmitChip(1, C(1, 0, 0, 3));    // 客户端真人槽 1：全布
            f.Pump();   // ChipSubmit(1) 送达主机（槽 1 已填）
            f.Pump();   // 主机全填 → 判定结算 → ChipBatch 广播；客户端消费重放

            Assert.That(f.Host.State.Phase, Is.EqualTo(TurnPhase.ActionPhase));
            Assert.That(f.Client.State.Phase, Is.EqualTo(TurnPhase.ActionPhase));
            Assert.That(f.Host.State.CurrentTick, Is.EqualTo(f.Client.State.CurrentTick));
            Assert.That(f.Host.State.ComputeStateHash(), Is.EqualTo(f.Client.State.ComputeStateHash()),
                "同批次输入两端判定结算必须逐位一致");
            Assert.That(f.Host.State.LastAllocations[0].Rock, Is.EqualTo(3));
            Assert.That(f.Host.State.LastAllocations[1].Paper, Is.EqualTo(3));
        }

        [Test]
        public void 部分提交加超时_超时槽自动111_结算()
        {
            var f = Fixture.Create(4, 1);
            f.Pump();   // 开窗
            f.Host.SubmitChip(0, C(0, 3, 0, 0));   // 槽 0 提交
            f.Pump();   // 收集（槽 1 未提交 → 暂停等待）

            f.Clock.Now += f.Config.JudgmentWindowMs + 1;   // 快进时钟越过窗口
            f.Pump();   // 主机超时 → 槽 1 SetDefault(1-1-1) → 结算 → ChipBatch 广播；客户端重放

            Assert.That(f.Host.State.Phase, Is.EqualTo(TurnPhase.ActionPhase));
            Assert.That(f.Client.State.Phase, Is.EqualTo(TurnPhase.ActionPhase));
            Assert.That(f.Host.State.LastAllocations[1].Rock, Is.EqualTo(1), "超时槽自动 1-1-1");
            Assert.That(f.Host.State.LastAllocations[1].Scissors, Is.EqualTo(1));
            Assert.That(f.Host.State.LastAllocations[1].Paper, Is.EqualTo(1));
            Assert.That(f.Host.State.ComputeStateHash(), Is.EqualTo(f.Client.State.ComputeStateHash()));
        }

        [Test]
        public void 全部超时_全员111_引擎保底进入行动()
        {
            var f = Fixture.Create(2, 1);   // 2 人局：槽 0、1 全真人，无 AI
            f.Pump();   // 开窗（等待 0/1 提交）
            f.Clock.Now += f.Config.JudgmentWindowMs + 1;
            f.Pump();   // 首轮超时：全员 1-1-1 → 全局平 → 作废重判（Phase 仍 JudgmentPhase）→ 广播
            Assert.That(f.Host.State.Phase, Is.EqualTo(TurnPhase.JudgmentPhase), "首轮全平应作废重判");
            Assert.That(f.Client.State.Phase, Is.EqualTo(TurnPhase.JudgmentPhase), "客户端重放同样作废重判");

            f.Clock.Now += f.Config.JudgmentWindowMs + 1;
            f.Pump();   // 主机重新开窗（新一轮 RoundStart）
            f.Clock.Now += f.Config.JudgmentWindowMs + 1;
            f.Pump();   // 第二轮超时：全员 1-1-1 → 连续平 2 次 → 保底 1 AP → ActionPhase → 广播；客户端重放

            Assert.That(f.Host.State.Phase, Is.EqualTo(TurnPhase.ActionPhase));
            Assert.That(f.Client.State.Phase, Is.EqualTo(TurnPhase.ActionPhase));
            Assert.That(f.Host.State.GetCharacterByPlayer(0).ActionPoints, Is.EqualTo(1), "保底每人 1 AP");
            Assert.That(f.Host.State.GetCharacterByPlayer(1).ActionPoints, Is.EqualTo(1));
            Assert.That(f.Host.State.ComputeStateHash(), Is.EqualTo(f.Client.State.ComputeStateHash()));
        }

        [Test]
        public void 客户端由GameStart消息触发开局_状态就绪()
        {
            var f = Fixture.Create(4, 1, startClientDirectly: false);
            Assert.That(f.Client.State, Is.Null, "未收到 GameStart 前客户端不得开局");

            f.Pump();   // 主机 Tick 开窗；GameStart 广播送达客户端 → 客户端开局

            Assert.That(f.Client.State, Is.Not.Null, "收到 GameStart 后客户端须开局");
            Assert.That(f.Client.State.Phase, Is.EqualTo(TurnPhase.JudgmentPhase));
            Assert.That(f.Host.State.Phase, Is.EqualTo(TurnPhase.JudgmentPhase));
            Assert.That(f.Client.State.CurrentTurn, Is.EqualTo(f.Host.State.CurrentTurn));
            Assert.That(f.Client.State.ComputeStateHash(), Is.EqualTo(f.Host.State.ComputeStateHash()),
                "GameStart 驱动两端同 seed/同角色表建世界，状态必须一致");
        }
    }
}
