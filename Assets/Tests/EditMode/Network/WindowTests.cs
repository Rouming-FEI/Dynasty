using System;
using System.Collections.Generic;
using Core;
using Core.Replay;
using Gameplay.Characters;
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

            /// <summary>广播消息类型日志（测试观察主机广播序列）。</summary>
            public readonly List<NetMsgType> BroadcastLog = new List<NetMsgType>();
            /// <summary>ActionTurn 广播载荷 (playerId, windowMs)——行动窗口轮换断言。</summary>
            public readonly List<(int Player, int WindowMs)> ActionTurnLog = new List<(int, int)>();
            /// <summary>ActionBatch 广播载荷 (playerId, isPass)——超时跳过断言。</summary>
            public readonly List<(int Player, int IsPass)> ActionBatchLog = new List<(int, int)>();

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
                NetMsgType type = (NetMsgType)buf[0];
                BroadcastLog.Add(type);
                if (type == NetMsgType.ActionTurn)
                {
                    var r = new NetDataReader(buf);
                    r.GetByte();   // header
                    ActionTurnLog.Add((r.GetInt(), r.GetInt()));
                }
                else if (type == NetMsgType.ActionBatch)
                {
                    var r = new NetDataReader(buf);
                    r.GetByte();   // header
                    ReplayActionRecord rec = NetProtocol.ReadActionRecord(r);
                    ActionBatchLog.Add((rec.PlayerId, r.GetByte()));
                }
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

        // ==================== 行动窗口（Task 8） ====================

        /// <summary>第一个可动行动者（与 LocalLockstep.GetNextActor 同语义：ActionOrder 序 + AP>0 或守护充能≥3）。</summary>
        private static int FirstActor(WorldState s)
        {
            int[] order = s.TurnManager.ActionOrder;
            for (int i = 0; i < order.Length; i++)
            {
                int pid = order[i];
                Character c = s.GetCharacterByPlayer(pid);
                if (c != null && !c.IsDead && (c.ActionPoints > 0 || c.ASC.GetGuardianCharge() >= 3))
                    return pid;
            }
            return -1;
        }

        /// <summary>推进到行动阶段并打开首行动者行动窗口（两端判定结算 + 客户端重放完成）。
        /// 筹码 (2,1,0)/(0,2,1) 双赢 1-1 配置：全员 1 AP，行动阶段有两位行动者。</summary>
        private static void ReachActionWindow(Fixture f)
        {
            f.Pump();   // 开判定窗
            f.Host.SubmitChip(0, C(0, 2, 1, 0));
            f.Client.SubmitChip(1, C(1, 0, 2, 1));
            f.Pump();   // ChipSubmit(1) 送达主机
            f.Pump();   // 判定结算 → ChipBatch 广播 → 客户端重放 → ActionPhase
            f.Pump();   // 主机开首行动者窗口（ActionTurn 广播）；客户端行动阶段等待
            Assert.That(f.Host.State.Phase, Is.EqualTo(TurnPhase.ActionPhase));
            Assert.That(f.Client.State.Phase, Is.EqualTo(TurnPhase.ActionPhase));
            Assert.That(f.Host.State.ComputeStateHash(), Is.EqualTo(f.Client.State.ComputeStateHash()));
        }

        [Test]
        public void 行动者提交Move命令_ActionBatch广播_两端执行位置一致()
        {
            var f = Fixture.Create(2, 1);
            ReachActionWindow(f);
            int actor = FirstActor(f.Host.State);
            Assert.That(actor, Is.GreaterThanOrEqualTo(0), "判定结算后应有可动行动者");

            Character ch = f.Host.State.GetCharacterByPlayer(actor);
            int from = ch.CurrentLocationId;
            int target = f.Host.State.Locations.GetConnectedLocations(from)[0];
            int apBefore = ch.ActionPoints;
            Assert.That(apBefore, Is.GreaterThan(0));

            var rec = new ReplayActionRecord { PlayerId = actor, Kind = ReplayActionKind.Move, IntArg1 = target };
            if (actor == 0) f.Host.SubmitAction(0, rec);
            else f.Client.SubmitAction(1, rec);
            f.Pump();   // ActionSubmit 送达主机 → 主机广播 ActionBatch → 注入执行
            f.Pump();   // 客户端消费重放

            Assert.That(f.Host.State.GetCharacterByPlayer(actor).CurrentLocationId, Is.EqualTo(target),
                "主机端移动应生效");
            Assert.That(f.Client.State.GetCharacterByPlayer(actor).CurrentLocationId, Is.EqualTo(target),
                "客户端重放后位置必须与主机一致");
            Assert.That(f.Host.State.GetCharacterByPlayer(actor).ActionPoints, Is.EqualTo(apBefore - 1),
                "移动消耗 1 AP");
            Assert.That(f.Client.State.GetCharacterByPlayer(actor).ActionPoints, Is.EqualTo(apBefore - 1));
            Assert.That(f.Host.State.ComputeStateHash(), Is.EqualTo(f.Client.State.ComputeStateHash()),
                "同一行动批次两端执行结果必须逐位一致");
            Assert.That(f.Host.State.Phase, Is.EqualTo(TurnPhase.ActionPhase), "另一行动者仍有 AP，行动阶段继续");
        }

        [Test]
        public void 行动者超时_isPass_WasteAP注入_AP清零轮换下一行动者()
        {
            var f = Fixture.Create(2, 1);
            ReachActionWindow(f);
            int actor = FirstActor(f.Host.State);
            int other = 1 - actor;
            int apBefore = f.Host.State.GetCharacterByPlayer(actor).ActionPoints;
            Assert.That(apBefore, Is.GreaterThan(0));

            f.Clock.Now += f.Config.ActionWindowMs + 1;   // 快进越过行动窗口
            for (int i = 0; i < apBefore; i++) f.Pump();  // 每个 AP 点一个 WasteAP 批次（isPass=1）
            f.Clock.Now = 0;                              // 复位时钟：轮换窗口不应立即再超时
            f.Pump();                                     // 主机开下一行动者窗口（ActionTurn）

            Assert.That(f.Host.State.GetCharacterByPlayer(actor).ActionPoints, Is.EqualTo(0),
                "超时 WasteAP 应清零该行动者 AP");
            Assert.That(f.Client.State.GetCharacterByPlayer(actor).ActionPoints, Is.EqualTo(0),
                "客户端重放同样清零");
            Assert.That(f.HostTransport.ActionBatchLog.Exists(e => e.Player == actor && e.IsPass == 1), Is.True,
                "超时跳过应广播 isPass=1 的 ActionBatch");
            Assert.That(f.HostTransport.ActionTurnLog.Exists(e => e.Player == other), Is.True,
                "该行动者 AP 清零后窗口应轮换到下一行动者");
            Assert.That(f.Host.State.GetCharacterByPlayer(other).ActionPoints, Is.EqualTo(1),
                "轮换后下一行动者 AP 未消耗");
            Assert.That(f.Host.State.Phase, Is.EqualTo(TurnPhase.ActionPhase), "另一行动者仍有 AP，行动阶段继续");
            Assert.That(f.Host.State.ComputeStateHash(), Is.EqualTo(f.Client.State.ComputeStateHash()));
        }

        [Test]
        public void 全部AP耗尽_TurnEndBatch广播_回合末结算_下一轮判定_两端哈希一致()
        {
            var f = Fixture.Create(2, 1);
            ReachActionWindow(f);
            int totalAp = f.Host.State.GetCharacterByPlayer(0).ActionPoints
                        + f.Host.State.GetCharacterByPlayer(1).ActionPoints;
            Assert.That(totalAp, Is.GreaterThan(0));

            f.Clock.Now += f.Config.ActionWindowMs + 1;
            // 全员超时 WasteAP 清空：每个行动者窗口需各自快进时钟（开窗 Tick 内超时判定
            // 以开窗时刻为基准，不会当 Tick 立即超时），直至回合末结算进入下一轮判定
            int guard = 0;
            while (f.Host.State.Phase == TurnPhase.ActionPhase && guard++ < 20)
            {
                f.Clock.Now += f.Config.ActionWindowMs + 1;
                f.Pump();
            }

            Assert.That(f.Host.State.Phase, Is.EqualTo(TurnPhase.JudgmentPhase), "回合末结算后进入下一轮判定");
            Assert.That(f.Client.State.Phase, Is.EqualTo(TurnPhase.JudgmentPhase), "客户端重放同样进入下一轮判定");
            Assert.That(f.Host.State.CurrentTurn, Is.EqualTo(2));
            Assert.That(f.Client.State.CurrentTurn, Is.EqualTo(2));
            Assert.That(f.Host.State.ComputeStateHash(), Is.EqualTo(f.Client.State.ComputeStateHash()),
                "完整一回合（判定→行动→回合末→下一轮判定）两端哈希必须一致");
            Assert.That(f.HostTransport.BroadcastLog.Contains(NetMsgType.TurnEndBatch), Is.True,
                "全部 AP 耗尽应广播 TurnEndBatch");
        }

        [Test]
        public void 行动阶段真人提交非法命令_PlayerId不匹配_主机拒绝不广播()
        {
            var f = Fixture.Create(2, 1);
            ReachActionWindow(f);
            int actor = FirstActor(f.Host.State);
            int other = 1 - actor;
            int apBefore = f.Host.State.GetCharacterByPlayer(actor).ActionPoints;

            // 提交者用自己槽位提交，但记录 PlayerId 指向另一槽 → 主机拒绝（不广播）
            var rec = new ReplayActionRecord { PlayerId = other, Kind = ReplayActionKind.Move, IntArg1 = 4 };
            if (actor == 0) f.Host.SubmitAction(0, rec);
            else f.Client.SubmitAction(1, rec);
            f.Pump();   // 提交送达主机 → 拒绝
            f.Pump();

            Assert.That(f.HostTransport.ActionBatchLog.Count, Is.EqualTo(0), "非法提交不得广播 ActionBatch");
            Assert.That(f.Host.LastError, Is.Not.Null, "主机应记录拒绝原因");
            Assert.That(f.Host.State.GetCharacterByPlayer(actor).ActionPoints, Is.EqualTo(apBefore),
                "拒绝后 AP 不变");
            Assert.That(f.Host.State.Phase, Is.EqualTo(TurnPhase.ActionPhase), "拒绝后窗口保持打开（行动阶段继续）");
            Assert.That(f.Host.State.ComputeStateHash(), Is.EqualTo(f.Client.State.ComputeStateHash()));
        }
    }
}
