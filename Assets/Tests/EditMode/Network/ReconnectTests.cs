using System;
using System.Collections.Generic;
using Core;
using Core.Replay;
using Core.Sync;
using Gameplay.Judgment;
using LiteNetLib.Utils;
using Network.Protocol;
using Network.Session;
using Network.Transport;
using NUnit.Framework;

namespace Dynasty.Tests.Network
{
    /// <summary>
    /// 断线重连（Task 11）：SessionLog 往返 / 主机快照 + HandleResyncRequest /
    /// 客户端断线重连重建重放追平（哈希一致）/ 断线槽冻结（窗口超时不清零，宽限期到回落 AI）。
    /// 用「本地双 Loop 同进程模拟」（内存转发消息）——重连追平的核心是快照+日志重放的确定性，
    /// 不依赖真实 UDP（真实路径由 Task 12 GameLoopNetTests 覆盖）。
    /// </summary>
    public class ReconnectTests
    {
        /// <summary>内存传输替身（与 WindowTests 同型）：SendReliable/Broadcast 写入 Outbound，
        /// DeliverAll 以本端 SelfPeerId 转发到对端 OnDataReceived。</summary>
        private sealed class LoopbackTransport : INetworkTransport
        {
            public LoopbackTransport Peer;
            public readonly int SelfPeerId;
            public readonly List<(int peer, byte[] data)> Outbound = new List<(int, byte[])>();

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
                return true;
            }

            public void DeliverAll()
            {
                var batch = Outbound.ToArray();
                Outbound.Clear();
                foreach (var (peer, data) in batch)
                    Peer.OnDataReceived?.Invoke(SelfPeerId, new NetDataReader(data));
            }

            /// <summary>测试注入：以指定 peerId 伪造对端消息直达本端 HandleMessage。</summary>
            public void InjectFrom(int peerId, NetDataWriter w)
            {
                var buf = new byte[w.Length];
                Array.Copy(w.Data, buf, w.Length);
                OnDataReceived?.Invoke(peerId, new NetDataReader(buf));
            }
        }

        /// <summary>可手动推进的时钟（控制窗口超时与重连宽限期）。</summary>
        private sealed class FakeClock
        {
            public long Now;
        }

        /// <summary>夹具：主机 Loop（槽 0 真人 + 对端真人 + AI 槽）与客户端 Loop 同进程配对。</summary>
        private sealed class Fixture
        {
            public SessionConfig Config;
            public FakeClock Clock;
            public LoopbackTransport HostTransport;
            public LoopbackTransport ClientTransport;
            public NetworkSession HostSession;
            public NetworkSession ClientSession;
            public NetworkGameLoop Host;
            public NetworkGameLoop Client;

            public static Fixture Create(int playerCount, int connectedClients)
            {
                var defIds = new int[playerCount];
                for (int i = 0; i < playerCount; i++) defIds[i] = i + 1;
                var config = new SessionConfig
                {
                    PlayerCount = playerCount,
                    RandomSeed = 42,
                    JudgmentWindowMs = 30000,
                    ActionWindowMs = 20000,
                    ReconnectGraceMs = 60000,
                    HostPlayerDefIds = defIds
                };

                var hostSession = new NetworkSession(config, true);
                for (int pid = 1; pid <= connectedClients; pid++)
                    Assert.That(hostSession.HandleHello(pid, pid), Is.EqualTo(pid));
                Assert.That(hostSession.TryStartGame(), Is.True);

                var clientSession = new NetworkSession(config, false);
                clientSession.ApplyJoinAccept(1, 42, defIds);

                var ht = new LoopbackTransport(0);
                var ct = new LoopbackTransport(1);
                ht.Peer = ct;
                ct.Peer = ht;

                var clock = new FakeClock();
                var host = new NetworkGameLoop(ht, hostSession, config);
                var client = new NetworkGameLoop(ct, clientSession, config);
                host.ClockProvider = () => clock.Now;
                client.ClockProvider = () => clock.Now;
                host.StartGame();
                client.StartGame();

                return new Fixture
                {
                    Config = config,
                    Clock = clock,
                    HostTransport = ht,
                    ClientTransport = ct,
                    HostSession = hostSession,
                    ClientSession = clientSession,
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
                    HostTransport.DeliverAll();
                    Client.Tick();
                    ClientTransport.DeliverAll();
                }
            }
        }

        // ==================== 重连端（客户端2）消息路由 ====================

        /// <summary>把 source 的 Outbound 逐条以 sourcePeerId 为发送方注入 dest（重连端传输与主机非同一 Pair，
        /// 复用 InjectFrom 手工路由；dest 的 OnDataReceived 同步触发，等效真实投递）。</summary>
        private static void RouteOutbound(LoopbackTransport source, LoopbackTransport dest, int sourcePeerId)
        {
            var batch = source.Outbound.ToArray();
            source.Outbound.Clear();
            foreach (var (_, data) in batch)
            {
                var w = new NetDataWriter();
                w.Put(data, 0, data.Length);
                dest.InjectFrom(sourcePeerId, w);
            }
        }

        /// <summary>重连端交替泵送：主机 Tick → 主机消息路由到客户端2 → 客户端2 Tick → 客户端2消息路由到主机
        /// （重连客户端2 与主机无 Peer 回链，不能用 Fixture.Pump 的 DeliverAll；追赶循环的
        /// 自动 ResyncRequest/ResyncReply 走此路径往返）。</summary>
        private static void PumpReconnect(Fixture f, NetworkGameLoop client2, LoopbackTransport transport2, int rounds = 1)
        {
            for (int r = 0; r < rounds; r++)
            {
                f.Host.Tick();
                RouteOutbound(f.HostTransport, transport2, 0);   // 主机 peer 0 → 客户端2
                client2.Tick();
                RouteOutbound(transport2, f.HostTransport, 7);   // 客户端2 peer 7 → 主机（重连新 peerId）
            }
        }

        private static ChipAllocation C(int playerId, int rock, int scissors, int paper)
        {
            var a = new ChipAllocation(playerId);
            a.Set(rock, scissors, paper);
            return a;
        }

        /// <summary>超时驱动推进：完成 (targetTurn-1) 个完整回合（判定/行动窗口逐次快进 + 泵送），
        /// 随后泵送到客户端 tick 与主机一致（客户端纯重放落后一拍属正常）。</summary>
        private static void AdvanceTurns(Fixture f, int targetTurn)
        {
            int guard = 0;
            while (f.Host.State.CurrentTurn < targetTurn && f.Host.State.Phase != TurnPhase.GameOver && guard++ < 2000)
            {
                if (f.Host.State.Phase == TurnPhase.JudgmentPhase)
                {
                    f.Clock.Now += f.Config.JudgmentWindowMs + 1;
                    f.Pump();
                }
                else if (f.Host.State.Phase == TurnPhase.ActionPhase)
                {
                    f.Clock.Now += f.Config.ActionWindowMs + 1;
                    f.Pump();
                }
                else
                {
                    f.Pump();   // TurnEnd 等防御路径
                }
            }
            Assert.That(f.Host.State.Phase, Is.Not.EqualTo(TurnPhase.GameOver), "推进期间不得意外终局");
            guard = 0;
            while (f.Client.State != null && f.Host.State != null
                   && f.Client.State.CurrentTick != f.Host.State.CurrentTick && guard++ < 500)
                f.Pump();
        }

        /// <summary>解析 ResyncReply 载荷（[snapshotTick][snapshotLen][snapshotBytes][logLen][logBytes]）。</summary>
        private static void ParseReply(byte[] payload, out int snapshotTick, out byte[] snapshotBytes, out byte[] logBytes)
        {
            var r = new NetDataReader(payload);
            NetProtocol.ReadResyncReply(r, out snapshotTick, out snapshotBytes, out int logLen, out logBytes);
        }

        // ==================== SessionLog（场景 1） ====================

        [Test]
        public void SessionLog_混合条目往返一致()
        {
            var log = new SessionLog();
            log.AddChipBatch(1, new[] { C(0, 3, 0, 0), C(1, 0, 0, 3) });
            log.AddAction(2, new ReplayActionRecord { Tick = 2, PlayerId = 0, Kind = ReplayActionKind.Move, IntArg1 = 4, Source = 1 });
            log.AddAction(3, new ReplayActionRecord { Tick = 3, PlayerId = 1, Kind = ReplayActionKind.WasteAP, Source = 255 });   // 超时标记
            Assert.That(log.Count, Is.EqualTo(3));

            byte[] bytes = log.SerializeFrom(0);
            List<SessionLog.Entry> entries = SessionLog.Deserialize(bytes);

            Assert.That(entries.Count, Is.EqualTo(3));
            Assert.That(entries[0].IsChipBatch, Is.True);
            Assert.That(entries[0].Tick, Is.EqualTo(1));
            Assert.That(entries[0].Chips.Length, Is.EqualTo(2));
            Assert.That(entries[0].Chips[0].Rock, Is.EqualTo(3));
            Assert.That(entries[0].Chips[1].Paper, Is.EqualTo(3));
            Assert.That(entries[1].IsChipBatch, Is.False);
            Assert.That(entries[1].Tick, Is.EqualTo(2));
            Assert.That(entries[1].Action.PlayerId, Is.EqualTo(0));
            Assert.That(entries[1].Action.Kind, Is.EqualTo(ReplayActionKind.Move));
            Assert.That(entries[1].Action.IntArg1, Is.EqualTo(4));
            Assert.That(entries[2].Action.Source, Is.EqualTo(255), "超时标记 Source=255 往返保留");
            Assert.That(entries[2].Action.Kind, Is.EqualTo(ReplayActionKind.WasteAP));
        }

        [Test]
        public void SessionLog_SerializeFrom截取tick之后条目()
        {
            var log = new SessionLog();
            log.AddChipBatch(0, new[] { C(0, 1, 1, 1) });
            log.AddAction(2, new ReplayActionRecord { Tick = 2, PlayerId = 0, Kind = ReplayActionKind.WasteAP });
            log.AddChipBatch(5, new[] { C(0, 2, 0, 0) });

            byte[] bytes = log.SerializeFrom(2);
            List<SessionLog.Entry> entries = SessionLog.Deserialize(bytes);

            Assert.That(entries.Count, Is.EqualTo(2), "仅保留 tick ≥ fromTick 的条目");
            Assert.That(entries[0].IsChipBatch, Is.False);
            Assert.That(entries[0].Tick, Is.EqualTo(2));
            Assert.That(entries[1].IsChipBatch, Is.True);
            Assert.That(entries[1].Tick, Is.EqualTo(5));
        }

        [Test]
        public void SessionLog_损坏数据拒绝()
        {
            var log = new SessionLog();
            log.AddChipBatch(1, new[] { C(0, 3, 0, 0) });
            byte[] bytes = log.SerializeFrom(0);
            bytes[0] ^= 0xFF;   // 篡改条目数前缀 → 长度校验拒绝
            Assert.Throws<System.IO.InvalidDataException>(() => SessionLog.Deserialize(bytes));
        }

        // ==================== 主机快照 + ResyncRequest（场景 2） ====================

        [Test]
        public void 主机侧_推进两回合_重连请求返回快照加日志()
        {
            var f = Fixture.Create(4, 1);
            AdvanceTurns(f, 3);   // 完成 2 回合 → CurrentTurn == 3

            int[] ticks = f.Host.Reconnect.SnapshotTicks;
            Assert.That(ticks.Length, Is.EqualTo(3), "开局锚点 + 最近 2 份回合末快照");
            Assert.That(ticks[0], Is.EqualTo(0), "开局锚点 tick=0");
            Assert.That(ticks[1], Is.LessThan(ticks[2]), "回合末快照 tick 单调递增");

            // lastTick=0 → 回退开局快照 + 全量日志
            byte[] p0 = f.Host.Reconnect.HandleResyncRequest(0, 0);
            Assert.That(p0, Is.Not.Null);
            ParseReply(p0, out int st0, out byte[] sb0, out byte[] lb0);
            Assert.That(st0, Is.EqualTo(0), "lastTick=0 应返回开局快照");
            WorldState snap0 = WorldStateSerializer.Deserialize(sb0);
            Assert.That(snap0.CurrentTurn, Is.EqualTo(1), "开局快照在第 1 回合判定前");
            List<SessionLog.Entry> log0 = SessionLog.Deserialize(lb0);
            Assert.That(log0.Count, Is.GreaterThan(0), "开局快照之后应有日志条目");

            // lastTick=第一回合末快照 → 返回中段快照 + 截取日志（少于全量）
            byte[] p1 = f.Host.Reconnect.HandleResyncRequest(ticks[1], 2);
            Assert.That(p1, Is.Not.Null);
            ParseReply(p1, out int st1, out byte[] sb1, out byte[] lb1);
            Assert.That(st1, Is.EqualTo(ticks[1]), "lastTick=回合末快照 tick → 返回该快照");
            List<SessionLog.Entry> log1 = SessionLog.Deserialize(lb1);
            Assert.That(log1.Count, Is.LessThan(log0.Count), "中段快照的日志应短于全量");
            Assert.That(log1.Count, Is.GreaterThan(0), "中段快照后仍有待重放条目");
            WorldState snap1 = WorldStateSerializer.Deserialize(sb1);
            Assert.That(snap1.CurrentTurn, Is.EqualTo(2), "第一回合末快照应在第 2 回合判定前");
        }

        // ==================== 客户端断线重连追平（场景 3） ====================

        [Test]
        public void 客户端断线_主机宽限后继续_重连重放追平哈希一致()
        {
            var f = Fixture.Create(4, 1);   // 槽 0 主机真人 + 槽 1 对端真人 + 槽 2/3 AI
            AdvanceTurns(f, 2);   // 完成 1 回合 → 客户端停在回合边界（tick=S1，turn 2 判定前）
            Assert.That(f.Host.State.ComputeStateHash(), Is.EqualTo(f.Client.State.ComputeStateHash()),
                "前置：断线前两端状态一致");

            // —— 断线：客户端冻结在 lastTick/lastTurn；主机槽 1 冻结 → 宽限期到 → 回落 AI → 继续推进 1 回合
            int lastTick = f.Client.State.CurrentTick;
            int lastTurn = f.Client.State.CurrentTurn;
            f.HostSession.HandleDisconnect(1);   // OnPlayerLeft → GameLoop 冻结槽 1
            f.Clock.Now += f.Config.ReconnectGraceMs + 1;   // 宽限期耗尽
            f.Pump();                             // 主机解冻 → 槽 1 回落 AI → 判定/行动继续
            AdvanceTurns(f, 3);                   // 主机再完成 1 回合（CurrentTurn 2 → 3）

            Assert.That(f.Host.State.CurrentTurn, Is.GreaterThan(lastTurn), "断线槽回落 AI 后主机继续推进");
            Assert.That(f.Host.Reconnect.SnapshotTicks.Length, Is.EqualTo(3), "开局锚点 + 两回合末快照");

            // —— 重连：用断线前的 (lastTick, lastTurn) 请求 → 返回快照+日志
            byte[] payload = f.Host.Reconnect.HandleResyncRequest(lastTick, lastTurn);
            Assert.That(payload, Is.Not.Null, "重连请求应返回快照+日志载荷");
            ParseReply(payload, out int snapTick, out byte[] snapBytes, out byte[] logBytes);
            Assert.That(snapTick, Is.EqualTo(lastTick), "应命中断线时所在的回合末快照（中段快照路径）");
            Assert.That(SessionLog.Deserialize(logBytes).Count, Is.GreaterThan(0), "快照后应有待重放日志");

            // —— 客户端重建重放（模拟重连后的新连接）
            var defIds = new int[f.Config.PlayerCount];
            for (int i = 0; i < defIds.Length; i++) defIds[i] = i + 1;
            var session2 = new NetworkSession(f.Config, false);
            session2.ApplyJoinAccept(1, 42, defIds);
            var transport2 = new LoopbackTransport(1);
            var client2 = new NetworkGameLoop(transport2, session2, f.Config);
            client2.ClockProvider = () => f.Clock.Now;

            Assert.That(client2.Reconnect.HandleResyncReply(payload), Is.True, "快照重建应成功");
            Assert.That(client2.Reconnect.IsResyncing, Is.True);
            Assert.That(client2.Reconnect.ResyncTargetTick, Is.GreaterThanOrEqualTo(snapTick), "重放目标 tick 不早于快照 tick");

            int guard = 0;
            // Task 11 复审 C1 改造后：重放耗尽自动发起下一轮 Resync（用当前世界位置）——经传输往返
            // 主机回复；本场景主机无干扰（停在回合 3 判定窗），首轮重放完、次轮日志为空即收敛
            while (client2.Reconnect.IsResyncing && guard++ < 500)
                PumpReconnect(f, client2, transport2);
            Assert.That(client2.Reconnect.IsResyncing, Is.False, "重放应在有限步内追平");
            Assert.That(client2.LastError, Is.Null, "重放期间不得进入错误状态");
            Assert.That(client2.State.ComputeStateHash(), Is.EqualTo(f.Host.State.ComputeStateHash()),
                "重放追平后哈希必须与主机一致（快照+日志重放确定性）");
        }

        // ==================== 断线槽冻结（场景 4） ====================

        [Test]
        public void 断线槽冻结_窗口超时不清零_宽限期到回落AI继续()
        {
            var f = Fixture.Create(2, 1);   // 槽 0/1 全真人（无 AI 干扰）
            f.Pump();                       // 开判定窗
            f.Host.SubmitChip(0, C(0, 3, 0, 0));   // 槽 0 提交
            f.Pump();                       // 收集：槽 1 未提交 → 暂停

            f.HostSession.HandleDisconnect(1);   // 断线 → 冻结槽 1
            int tickBefore = f.Host.State.CurrentTick;
            f.Clock.Now += f.Config.JudgmentWindowMs + 1;   // 判定窗超时
            f.Pump();
            f.Pump();

            Assert.That(f.Host.State.Phase, Is.EqualTo(TurnPhase.JudgmentPhase), "冻结槽未归 → 判定窗口不得结算");
            Assert.That(f.Host.State.CurrentTick, Is.EqualTo(tickBefore), "冻结等待期间不得推进 Tick");
            Assert.That(f.Host.State.LastAllocations[1].Total, Is.EqualTo(0), "冻结槽不得被 1-1-1 超时填充");

            // 宽限期到 → 解冻 → 槽 1 回落 AI → 判定结算 → 行动阶段
            f.Clock.Now += f.Config.ReconnectGraceMs + 1;
            int guard = 0;
            while (f.Host.State.Phase == TurnPhase.JudgmentPhase && guard++ < 10)
                f.Pump();

            Assert.That(f.Host.State.Phase, Is.EqualTo(TurnPhase.ActionPhase), "解冻后槽回落 AI，判定结算进入行动阶段");
            Assert.That(f.Host.State.CurrentTick, Is.GreaterThan(tickBefore), "解冻后对局恢复推进");
        }

        // ==================== 消息路径：ResyncRequest → ResyncReply（重连路由） ====================

        [Test]
        public void 主机收到ResyncRequest_回复ResyncReply_重连槽位解冻()
        {
            var f = Fixture.Create(2, 1);
            AdvanceTurns(f, 2);

            // 断线 → 冻结槽 1
            f.HostSession.HandleDisconnect(1);
            Assert.That(f.Host.IsSlotFrozen(1), Is.True, "断线后槽 1 应被冻结");

            // 客户端重连：以新 peerId 发送 ResyncRequest（含 playerId=1）→ 主机 RejoinSlot + 回复
            var w = new NetDataWriter();
            NetProtocol.WriteHeader(w, NetMsgType.ResyncRequest);
            NetProtocol.WriteResyncRequest(w, f.Client.State.CurrentTick, f.Client.State.CurrentTurn, 1);
            f.HostTransport.InjectFrom(7, w);   // 新 peerId=7 模拟重连（HandleMessage 同步路由）

            Assert.That(f.Host.IsSlotFrozen(1), Is.False, "重连后槽 1 应解冻");
            Assert.That(f.HostSession.PlayerPeerIds[1], Is.EqualTo(7), "槽 1 应由新 peerId 重新持有");
            Assert.That(f.HostTransport.Outbound.Exists(m => m.peer == 7 && (NetMsgType)m.data[0] == NetMsgType.ResyncReply),
                Is.True, "主机应向重连 peer 回复 ResyncReply");
            f.Pump();   // 泵送（消息随后投递给客户端，Outbound 清空）
        }

        // ==================== Task 11 复审 C1：追赶循环竞态 ====================

        /// <summary>创建全新重连客户端2（无状态会话，经 ApplyJoinAccept 入座）。</summary>
        private static NetworkGameLoop CreateReconnectClient(Fixture f, out LoopbackTransport transport2)
        {
            var defIds = new int[f.Config.PlayerCount];
            for (int i = 0; i < defIds.Length; i++) defIds[i] = i + 1;
            var session2 = new NetworkSession(f.Config, false);
            session2.ApplyJoinAccept(1, 42, defIds);
            transport2 = new LoopbackTransport(1);
            var client2 = new NetworkGameLoop(transport2, session2, f.Config);
            client2.ClockProvider = () => f.Clock.Now;
            return client2;
        }

        /// <summary>
        /// 场景 A（行动阶段竞态，C1）：断线槽冻结期间主机推进其他行动者产生广播（槽 0 行动窗
        /// 超时 → WasteAP 批次在首轮 reply 之后广播）→ 客户端重放完日志后自动发起第二轮 Resync
        /// → 收到含新批次的日志 → 追平 → 哈希与主机一致。
        /// 时序：回合 2 判定结算后（槽 0 唯一胜者先行动）客户端断线于行动阶段；重连以全新会话
        /// 锚点全量重放；重放期间槽 0 超时 WasteAP 广播被客户端丢弃（追平中）——追赶循环第二轮
        /// 的 reply 日志覆盖该批次，追平后哈希一致（否则判定/行动校验 fault 或永久等待）。
        /// </summary>
        [Test]
        public void 场景A_行动阶段竞态_重放期间新广播被追赶循环覆盖()
        {
            var f = Fixture.Create(4, 1);   // 槽 0 主机真人 + 槽 1 对端真人 + 槽 2/3 AI
            AdvanceTurns(f, 2);             // 主机停在回合 2 判定（tick 3）
            f.Pump();                       // 开回合 2 判定窗
            f.Client.SubmitChip(1, C(1, 0, 3, 0));   // 客户端提交（剪刀 3）
            f.Host.SubmitChip(0, C(0, 3, 0, 0));     // 主机提交（石头 3 → 唯一胜者，先行动）
            int guard = 0;
            while (f.Host.State.Phase == TurnPhase.JudgmentPhase && guard++ < 10)
                f.Pump();
            Assert.That(f.Host.State.Phase, Is.EqualTo(TurnPhase.ActionPhase), "构造前提：判定应结算进入行动阶段");
            Assert.That(f.Host.State.TurnManager.ActionOrder[0], Is.EqualTo(0),
                "构造前提：槽 0 先行动（其行动窗等待输入，超时产生竞态广播）");
            int hostTickAtDisconnect = f.Host.State.CurrentTick;
            Assert.That(hostTickAtDisconnect, Is.EqualTo(4), "构造前提：断线于行动阶段非快照边界 tick（首轮日志非空）");

            // —— 断线：客户端冻结；主机冻结槽 1（行动窗继续等待槽 0 输入）
            f.Client.NotifyDisconnected();
            f.HostSession.HandleDisconnect(1);
            Assert.That(f.Host.IsSlotFrozen(1), Is.True);

            // —— 重连（全新会话 → 锚点全量重放）：初始请求经消息路径 → 主机回复（anchor + 日志）
            NetworkGameLoop client2 = CreateReconnectClient(f, out LoopbackTransport transport2);
            client2.NotifyDisconnected();   // 无状态 → 断线锚点 0（全量重放兜底）
            client2.SendResyncRequest();
            RouteOutbound(transport2, f.HostTransport, 7);   // 初始请求 → 主机（RejoinSlot + 回复）
            RouteOutbound(f.HostTransport, transport2, 0);   // 回复 → 客户端2（重建进入追平）
            Assert.That(client2.Reconnect.IsResyncing, Is.True, "初始回复后应进入追平");
            Assert.That(client2.Reconnect.ResyncTargetTick, Is.GreaterThanOrEqualTo(hostTickAtDisconnect));

            // —— 竞态：重放期间主机槽 0 行动窗超时 → WasteAP 广播（首轮 reply 日志之外的新批次）
            f.Clock.Now += f.Config.ActionWindowMs + 1;
            guard = 0;
            while (client2.Reconnect.IsResyncing && guard++ < 500)
                PumpReconnect(f, client2, transport2);

            Assert.That(client2.Reconnect.IsResyncing, Is.False, "追赶循环应在有限步内追平");
            Assert.That(client2.LastError, Is.Null, "重放期间不得进入错误状态");
            Assert.That(client2.State.ComputeStateHash(), Is.EqualTo(f.Host.State.ComputeStateHash()),
                "追赶循环追平后哈希必须与主机一致（重放期间广播被第二轮日志覆盖）");
        }

        /// <summary>
        /// 场景 B（判定阶段竞态，C1）：冻结槽断线前已提交筹码，其他真人槽（槽 0）补齐导致判定收口
        /// → ChipBatch 在重放期间广播（首轮 reply 日志之外）→ 客户端追赶循环第二轮日志覆盖 → 追平哈希一致。
        /// 时序：回合 2 判定窗内客户端提交后断线（槽 1 冻结但已提交，不阻塞判定收口）；重连以全新
        /// 会话锚点全量重放；重放期间槽 0 补齐 → 判定结算 → ChipBatch 广播被客户端丢弃（追平中）——
        /// 追赶循环第二轮 reply 日志含该 ChipBatch，重放追平（否则判定阶段空队列永久等待）。
        /// </summary>
        [Test]
        public void 场景B_判定阶段竞态_重放期间ChipBatch被追赶循环覆盖()
        {
            var f = Fixture.Create(4, 1);
            AdvanceTurns(f, 2);   // 主机停在回合 2 判定（tick 3）
            f.Pump();             // 开回合 2 判定窗
            // 客户端断线前已提交筹码（冻结槽不阻塞判定收口）
            f.Client.SubmitChip(1, C(1, 0, 3, 0));
            f.Pump();
            Assert.That(f.Client.State.CurrentTick, Is.EqualTo(3), "构造前提：断线于回合 2 判定（边界）");
            f.Client.NotifyDisconnected();
            f.HostSession.HandleDisconnect(1);
            Assert.That(f.Host.IsSlotFrozen(1), Is.True, "槽 1 应冻结");
            Assert.That(f.Host.State.Phase, Is.EqualTo(TurnPhase.JudgmentPhase), "判定尚未收口（槽 0 未补齐）");

            // —— 重连（全新会话 → 锚点全量重放）：初始请求 → 主机回复（anchor + 日志，不含回合 2 的 ChipBatch）
            NetworkGameLoop client2 = CreateReconnectClient(f, out LoopbackTransport transport2);
            client2.NotifyDisconnected();
            client2.SendResyncRequest();
            RouteOutbound(transport2, f.HostTransport, 7);
            RouteOutbound(f.HostTransport, transport2, 0);
            Assert.That(client2.Reconnect.IsResyncing, Is.True, "初始回复后应进入追平");

            // —— 竞态：重放期间其他真人槽（槽 0）补齐 → 判定收口 → ChipBatch 广播（首轮日志之外）
            f.Host.SubmitChip(0, C(0, 3, 0, 0));
            int guard = 0;
            while (client2.Reconnect.IsResyncing && guard++ < 500)
                PumpReconnect(f, client2, transport2);

            Assert.That(client2.Reconnect.IsResyncing, Is.False, "追赶循环应在有限步内追平");
            Assert.That(client2.LastError, Is.Null, "重放期间不得进入错误状态");
            Assert.That(client2.State.ComputeStateHash(), Is.EqualTo(f.Host.State.ComputeStateHash()),
                "重放期间判定收口的 ChipBatch 必须被追赶循环第二轮日志覆盖，追平哈希一致");
        }

        // ==================== Task 11 复审 I1：RejoinSlot 失败拒绝回复 ====================

        /// <summary>非法 playerId / 槽被其他对端持有的 ResyncRequest → 主机不回复 ResyncReply
        /// （拒绝重连），槽位保持冻结/原持有者不变。</summary>
        [Test]
        public void 主机收到非法重连请求_拒绝回复ResyncReply()
        {
            var f = Fixture.Create(2, 1);
            AdvanceTurns(f, 2);
            f.HostSession.HandleDisconnect(1);   // 槽 1 释放并冻结
            Assert.That(f.Host.IsSlotFrozen(1), Is.True);

            // —— 非法 playerId（越界）→ 拒绝，不回复
            var w = new NetDataWriter();
            NetProtocol.WriteHeader(w, NetMsgType.ResyncRequest);
            NetProtocol.WriteResyncRequest(w, 0, 1, 99);
            f.HostTransport.InjectFrom(7, w);
            Assert.That(f.HostTransport.Outbound, Is.Empty, "RejoinSlot 失败的重连请求不得回复 ResyncReply");
            Assert.That(f.Host.LastError, Is.Not.Null.And.Contain("RejoinSlot"), "主机应报告重连拒绝");
            Assert.That(f.HostSession.PlayerPeerIds[1], Is.EqualTo(-1), "非法重连不得占用槽位");
            Assert.That(f.Host.IsSlotFrozen(1), Is.True, "拒绝重连后槽位保持冻结");

            // —— 合法重连（peer 7 拿回槽 1）→ 回复
            var w2 = new NetDataWriter();
            NetProtocol.WriteHeader(w2, NetMsgType.ResyncRequest);
            NetProtocol.WriteResyncRequest(w2, 0, 1, 1);
            f.HostTransport.InjectFrom(7, w2);
            Assert.That(f.Host.IsSlotFrozen(1), Is.False, "合法重连应解冻槽位");
            Assert.That(f.HostSession.PlayerPeerIds[1], Is.EqualTo(7), "槽 1 应由重连 peer 持有");
            Assert.That(f.HostTransport.Outbound.Count, Is.EqualTo(1), "合法重连应回复一次 ResyncReply");

            // —— 槽被其他对端持有（peer 8 冒名重连槽 1）→ 拒绝，不回复
            var w3 = new NetDataWriter();
            NetProtocol.WriteHeader(w3, NetMsgType.ResyncRequest);
            NetProtocol.WriteResyncRequest(w3, 0, 1, 1);
            f.HostTransport.InjectFrom(8, w3);
            Assert.That(f.HostTransport.Outbound.Count, Is.EqualTo(1), "槽被占时不得再回复（Outbound 未增加）");
            Assert.That(f.HostSession.PlayerPeerIds[1], Is.EqualTo(7), "槽 1 仍由原持有 peer 7 持有");
            f.Pump();   // 泵送清空 Outbound
        }

        // ==================== Task 11 复审 I2：重建失败置 _clientFaulted ====================

        /// <summary>坏快照（篡改头部哈希 → 自校验失败）的 ResyncReply → 重建失败 → 客户端立即
        /// _clientFaulted（与协议校验失败同语义，停止重放，不再用坏数据推进状态机）。</summary>
        [Test]
        public void 重连快照哈希损坏_客户端进入错误状态()
        {
            var f = Fixture.Create(4, 1);
            AdvanceTurns(f, 2);

            // 组装含坏快照的 ResyncReply 载荷（篡改快照头部哈希字段 → Deserialize 自校验抛异常）
            byte[] payload = f.Host.Reconnect.HandleResyncRequest(0, 0);
            ParseReply(payload, out int snapTick, out byte[] snapBytes, out byte[] logBytes);
            var badSnap = new byte[snapBytes.Length];
            Array.Copy(snapBytes, badSnap, snapBytes.Length);
            badSnap[10] ^= 0xFF;   // 字节 8..15 为 ComputeStateHash 字段（Magic 4 + Version 4 之后）

            NetworkGameLoop client2 = CreateReconnectClient(f, out LoopbackTransport transport2);
            var w = new NetDataWriter();
            NetProtocol.WriteHeader(w, NetMsgType.ResyncReply);
            NetProtocol.WriteResyncReply(w, snapTick, badSnap, badSnap.Length, logBytes, logBytes.Length);
            transport2.InjectFrom(0, w);   // 伪造主机回复（HandleMessage 同步路由）

            Assert.That(client2.IsClientFaulted, Is.True, "坏快照重建失败应置 _clientFaulted");
            Assert.That(client2.LastError, Is.Not.Null, "应报告重建失败");
            Assert.That(client2.Reconnect.IsResyncing, Is.False, "失败后不得停留在追平状态");
            Assert.That(client2.State, Is.Null, "重建失败不得留下半初始化状态");
        }
    }
}
