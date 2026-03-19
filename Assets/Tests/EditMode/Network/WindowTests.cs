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
        /// 点对点（host↔client），点对点转发不需要真实 UDP。
        /// peerId 语义与真实传输一致：DeliverAll 以本端 SelfPeerId 作为发送方标识（主机恒 0，
        /// 客户端 = 其 playerId），主机据此做槽位持有者校验（I1）；InjectFrom 可伪造任意
        /// peerId 注入消息（冒名攻击测试用）。</summary>
        private sealed class LoopbackTransport : INetworkTransport
        {
            public LoopbackTransport Peer;
            /// <summary>本端在会话中的 peerId（主机恒 0；客户端 = playerId，Fixture 中为 1）。</summary>
            public readonly int SelfPeerId;
            public readonly List<(int peer, byte[] data)> Outbound = new List<(int, byte[])>();

            /// <summary>广播消息类型日志（测试观察主机广播序列）。</summary>
            public readonly List<NetMsgType> BroadcastLog = new List<NetMsgType>();
            /// <summary>ActionTurn 广播载荷 (playerId, windowMs)——行动窗口轮换断言。</summary>
            public readonly List<(int Player, int WindowMs)> ActionTurnLog = new List<(int, int)>();
            /// <summary>ActionBatch 广播载荷 (playerId, isPass, kind)——超时跳过/AI 行动断言。</summary>
            public readonly List<(int Player, int IsPass, ReplayActionKind Kind)> ActionBatchLog = new List<(int, int, ReplayActionKind)>();

            public event Action<int, NetDataReader> OnDataReceived;

            public LoopbackTransport(int selfPeerId)
            {
                SelfPeerId = selfPeerId;
            }

            public bool IsHost { get; }

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
                    ActionBatchLog.Add((rec.PlayerId, r.GetByte(), rec.Kind));
                }
                return true;
            }

            /// <summary>把本端 Outbound 全部转发到对端并清空（以本端 SelfPeerId 作为发送方）。</summary>
            public void DeliverAll()
            {
                var batch = Outbound.ToArray();
                Outbound.Clear();
                foreach (var (peer, data) in batch)
                    Peer.OnDataReceived?.Invoke(SelfPeerId, new NetDataReader(data));
            }

            /// <summary>测试注入：以指定 peerId 伪造对端消息直达本端 HandleMessage（冒名攻击测试用）。</summary>
            public void InjectFrom(int peerId, NetDataWriter w)
            {
                var buf = new byte[w.Length];
                Array.Copy(w.Data, buf, w.Length);
                OnDataReceived?.Invoke(peerId, new NetDataReader(buf));
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

                var ht = new LoopbackTransport(0);   // 主机 peerId 恒 0
                var ct = new LoopbackTransport(1);   // 客户端 peerId = playerId 1（会话槽 1 由 peer 1 持有）
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

        // ==================== 身份校验（Task 8 审查 I1） ====================

        /// <summary>构造一条指定槽位的 ChipSubmit 消息（turn = 提交方判定轮次；载荷不含 peerId，发送方由传输层标识）。</summary>
        private static NetDataWriter MakeChipSubmit(int turn, int playerId, int rock, int scissors, int paper)
        {
            var w = new NetDataWriter();
            NetProtocol.WriteHeader(w, NetMsgType.ChipSubmit);
            NetProtocol.WriteChipSubmit(w, turn, playerId, rock, scissors, paper);
            return w;
        }

        /// <summary>构造一条指定槽位的 ActionSubmit 消息。</summary>
        private static NetDataWriter MakeActionSubmit(ReplayActionRecord rec)
        {
            var w = new NetDataWriter();
            NetProtocol.WriteHeader(w, NetMsgType.ActionSubmit);
            NetProtocol.WriteActionSubmit(w, rec);
            return w;
        }

        [Test]
        public void 判定窗口冒名提交_持有者不符_主机拒绝不填充()
        {
            var f = Fixture.Create(4, 1);   // 槽 0/1 真人（peer 0/1 持有），槽 2/3 AI
            f.Pump();                       // 开判定窗

            // 冒名 1：伪造 peerId=9（不持有任何槽）提交槽 1 筹码
            f.HostTransport.InjectFrom(9, MakeChipSubmit(f.Host.State.CurrentTurn, 1, 0, 0, 3));
            // 冒名 2：真客户端 peer（peerId=1）冒充主机本地槽 0 提交（槽 0 恒 -1，网络提交天然拒绝）
            f.HostTransport.InjectFrom(1, MakeChipSubmit(f.Host.State.CurrentTurn, 0, 3, 0, 0));
            f.Pump();

            Assert.That(f.Host.LastError, Does.Contain("持有者"), "冒名提交应被记录槽位持有者不符的拒绝原因");
            Assert.That(f.Host.State.Phase, Is.EqualTo(TurnPhase.JudgmentPhase), "拒绝后判定窗口保持打开");

            // 冒名不影响正常提交路径：槽 0/1 正常提交 → 结算 → 行动阶段（两端哈希一致，
            // 若冒名未被拦截会以错误筹码结算导致客户端重放分叉，此处可捕捉回归）
            f.Host.SubmitChip(0, C(0, 2, 1, 0));
            f.Client.SubmitChip(1, C(1, 0, 2, 1));
            f.Pump();
            f.Pump();
            Assert.That(f.Host.State.Phase, Is.EqualTo(TurnPhase.ActionPhase), "正常提交结算不受冒名干扰");
            Assert.That(f.Host.State.ComputeStateHash(), Is.EqualTo(f.Client.State.ComputeStateHash()),
                "冒名被忽略后两端判定结果必须一致");
        }

        [Test]
        public void 行动窗口冒名提交_持有者不符_主机拒绝不广播()
        {
            var f = Fixture.Create(2, 1);   // 槽 0/1 全真人（无 AI 广播干扰）
            ReachActionWindow(f);
            int actor = FirstActor(f.Host.State);
            int other = 1 - actor;
            int apBefore = f.Host.State.GetCharacterByPlayer(actor).ActionPoints;

            // 冒名：伪造 peerId=9 提交当前行动者的 Move（持有者不符）→ 主机拒绝，不广播
            f.HostTransport.InjectFrom(9, MakeActionSubmit(
                new ReplayActionRecord { PlayerId = actor, Kind = ReplayActionKind.Move, IntArg1 = 4 }));
            f.Pump();

            Assert.That(f.Host.LastError, Does.Contain("持有者"), "冒名行动提交应被记录槽位持有者不符的拒绝原因");
            Assert.That(f.HostTransport.ActionBatchLog.Count, Is.EqualTo(0), "冒名提交不得广播 ActionBatch");
            Assert.That(f.Host.State.GetCharacterByPlayer(actor).ActionPoints, Is.EqualTo(apBefore), "拒绝后 AP 不变");
            Assert.That(f.Host.State.Phase, Is.EqualTo(TurnPhase.ActionPhase), "拒绝后行动窗口保持打开");
            Assert.That(f.Host.State.ComputeStateHash(), Is.EqualTo(f.Client.State.ComputeStateHash()));

            // 冒名不阻塞正常提交：真实持有者提交 → 广播并执行
            Character ch = f.Host.State.GetCharacterByPlayer(actor);
            int target = f.Host.State.Locations.GetConnectedLocations(ch.CurrentLocationId)[0];
            var rec = new ReplayActionRecord { PlayerId = actor, Kind = ReplayActionKind.Move, IntArg1 = target };
            if (actor == 0) f.Host.SubmitAction(0, rec);
            else f.Client.SubmitAction(1, rec);
            f.Pump();
            f.Pump();
            Assert.That(f.HostTransport.ActionBatchLog.Count, Is.EqualTo(1), "正常提交应广播 ActionBatch");
            Assert.That(f.Host.State.ComputeStateHash(), Is.EqualTo(f.Client.State.ComputeStateHash()),
                "冒名被忽略后正常行动路径两端仍同步");
        }

        // ==================== 过期提交防御（Task 12 复审 I3） ====================

        /// <summary>构造一条指定轮次的 ChipBatch 消息（turn = 主机判定结算轮次；载荷数组长度必须 = 槽位数）。</summary>
        private static NetDataWriter MakeChipBatch(int turn, ChipAllocation[] allocs)
        {
            var w = new NetDataWriter();
            NetProtocol.WriteHeader(w, NetMsgType.ChipBatch);
            NetProtocol.WriteChipBatch(w, turn, allocs);
            return w;
        }

        [Test]
        public void 过期ChipSubmit_判定轮次不匹配_主机拒绝且窗口保持_正常提交仍结算()
        {
            var f = Fixture.Create(4, 1);
            f.Pump();   // 开判定窗（等待槽 0/1 提交）

            // 过期提交：turn = 主机当前判定轮次 - 1（模拟客户端落后整回合重发旧筹码），
            // 由槽 1 真实持有者 peer 1 提交 → 槽位持有者校验通过、轮次校验拒绝
            // （内存传输下提交方 turn 恒等于主机轮次，此防御路径不会自然触发，须显式构造）
            f.HostTransport.InjectFrom(1, MakeChipSubmit(f.Host.State.CurrentTurn - 1, 1, 0, 0, 3));
            f.Pump();

            Assert.That(f.Host.LastError, Does.Contain("判定提交轮次不匹配"), "过期提交应被记录轮次不匹配的拒绝原因");
            Assert.That(f.Host.State.Phase, Is.EqualTo(TurnPhase.JudgmentPhase), "拒绝后判定窗口保持打开（未结算）");
            Assert.That(f.HostTransport.BroadcastLog.Contains(NetMsgType.ChipBatch), Is.False, "过期提交不得触发结算广播");

            // 拒绝不破坏窗口：槽 0/1 正常提交 → 结算 → 行动阶段（两端哈希一致）
            f.Host.SubmitChip(0, C(0, 3, 0, 0));
            f.Client.SubmitChip(1, C(1, 0, 0, 3));
            f.Pump();
            f.Pump();
            Assert.That(f.Host.State.Phase, Is.EqualTo(TurnPhase.ActionPhase), "正常提交结算不受过期提交干扰");
            Assert.That(f.Host.State.ComputeStateHash(), Is.EqualTo(f.Client.State.ComputeStateHash()),
                "过期提交被忽略后两端判定结果必须一致");
        }

        [Test]
        public void 过期ChipBatch_判定轮次过期_客户端进入错误状态停止重放()
        {
            var f = Fixture.Create(4, 1);
            f.Pump();   // 开窗：客户端保持当前判定轮次等待批次

            // 伪造主机（peerId=0）注入 turn = 客户端 CurrentTurn - 1 的过期批次
            // （真实 UDP 下过期批次由入队轮次校验拦截；内存传输下 turn 恒相等不会触发，此处显式构造）
            var allocs = new ChipAllocation[f.Client.State.PlayerCount];
            for (int i = 0; i < allocs.Length; i++)
                allocs[i] = new ChipAllocation(i);
            f.ClientTransport.InjectFrom(0, MakeChipBatch(f.Client.State.CurrentTurn - 1, allocs));
            f.Pump();

            Assert.That(f.Client.IsClientFaulted, Is.True, "过期批次应使客户端进入错误状态");
            Assert.That(f.Client.LastError, Does.Contain("轮次过期"), "应记录批次轮次过期的错误原因");

            // faulted 后停止重放：注入正确轮次的批次也不得消费推进（HandleMessage/TickClient 均已短路）
            int tickBefore = f.Client.State.CurrentTick;
            f.ClientTransport.InjectFrom(0, MakeChipBatch(f.Client.State.CurrentTurn, allocs));
            f.Pump();
            Assert.That(f.Client.State.CurrentTick, Is.EqualTo(tickBefore), "faulted 后停止重放，后续批次不得推进客户端");
        }

        // ==================== AI 槽行动路径（Task 8 审查 I3） ====================

        [Test]
        public void 四人混合局AI槽行动_经OnActionCaptured广播ActionBatch_客户端重放两端一致()
        {
            // 4 人局：槽 0（主机真人）+ 槽 1（对端真人）+ 槽 2/3（AI）。
            // AI 决策用独立随机流（seed = playerId*1000+7，固定可复现）；两端经
            // 「主机 AI 决策 → 广播 ActionBatch → 客户端重放」同一批次序列推进。
            var f = Fixture.Create(4, 1);
            Assert.That(f.HostTransport.ActionBatchLog.Count, Is.EqualTo(0), "开局不得有行动广播");

            // 多回合推进直到捕获 AI 槽行动批次：判定轮真人超时 1-1-1（AI 槽由
            // LocalLockstep 独立随机流收集），行动轮真人超时跳过 / AI 槽主机驱动。
            // 兜底多回合：某回合 AI 槽判定 0 AP 时行动阶段无 AI 行动者，循环进下一回合。
            int guard = 0;
            bool captured = false;
            (int Player, int IsPass, ReplayActionKind Kind) aiBatch = default;
            while (guard++ < 20 && !captured)
            {
                int jg = 0;
                while (f.Host.State.Phase == TurnPhase.JudgmentPhase && jg++ < 12)
                {
                    f.Clock.Now += f.Config.JudgmentWindowMs + 1;
                    f.Pump();
                }
                if (f.Host.State.Phase == TurnPhase.GameOver) break;

                int ag = 0;
                while (f.Host.State.Phase == TurnPhase.ActionPhase && ag++ < 24)
                {
                    f.Clock.Now += f.Config.ActionWindowMs + 1;
                    int before = f.HostTransport.ActionBatchLog.Count;
                    f.Pump();
                    for (int i = before; i < f.HostTransport.ActionBatchLog.Count; i++)
                    {
                        var e = f.HostTransport.ActionBatchLog[i];
                        if (e.Player >= 2 && e.IsPass == 0)   // AI 槽决策行动（非超时）
                        {
                            aiBatch = e;
                            captured = true;
                        }
                    }
                    if (captured) break;
                }
                if (f.Host.State.Phase == TurnPhase.GameOver) break;
            }

            Assert.That(captured, Is.True, "应在若干回合内捕获 AI 槽的 ActionBatch 广播");
            Assert.That(aiBatch.Player, Is.InRange(2, 3), "AI 行动批次 PlayerId 应为 AI 槽 2 或 3");
            Assert.That(aiBatch.IsPass, Is.EqualTo(0), "AI 决策行动 isPass 必须为 0");
            Assert.That(ReplayActionFactory.Create(
                new ReplayActionRecord { PlayerId = aiBatch.Player, Kind = aiBatch.Kind }, 0), Is.Not.Null,
                "AI 行动命令 Kind 必须可解析（Move/UseSkill 等，保底 WasteAP）");

            f.Pump();   // 客户端消费 AI 批次重放（捕获的同一 Pump 已 deliver，此处兜底同步一拍）
            Assert.That(f.Client.LastError, Is.Null, "客户端重放 AI 批次不得进入错误状态");
            Assert.That(f.Host.State.ComputeStateHash(), Is.EqualTo(f.Client.State.ComputeStateHash()),
                "AI 行动批次客户端重放后两端状态必须逐位一致");
        }
    }
}
