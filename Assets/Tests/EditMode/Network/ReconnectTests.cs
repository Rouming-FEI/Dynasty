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
            while (client2.Reconnect.IsResyncing && guard++ < 500)
                client2.Tick();
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
    }
}
