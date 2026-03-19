using System;
using System.Collections.Generic;
using AI;
using Core;
using Core.GAS;
using Core.Replay;
using Gameplay.Characters;
using Gameplay.Judgment;
using Gameplay.Locations;
using Network.Local;
using Network.Session;
using Network.Transport;
using NUnit.Framework;

namespace Dynasty.Tests.Network
{
    /// <summary>
    /// Task 12：完整对局一致性集成测试（真实 UDP 回环）。
    /// 1 真人网络玩家（客户端，槽 1）+ 3 AI（主机槽 2/3）+ 主机本地真人槽 0，经真实 UDP 127.0.0.1
    /// 完整对局至 GameOver：客户端终局哈希 == 主机终局哈希 == 纯本地对照局（同 seed/同角色/同输入脚本）哈希。
    /// 场景：
    ///   1. 完整对局一致 + 每 5 回合哈希抽查（防偏移早发现，回合边界主机==客户端，每 5 回合对对照局）
    ///   2. 中途重连一致：对局中段客户端 Stop + 重建 transport 重连 → Resync → 追平 → 继续至终局哈希一致
    ///   3. 判定/行动窗口超时路径在真实 UDP 下跑通（ClockProvider 快进触发超时，客户端跳过提交）
    /// 输入策略（网络与对照局共用同一纯函数，保证决策一致）：
    ///   判定：槽 0/1 按 (turn+playerId)%3 轮换 3-0-0 / 0-3-0 / 0-0-3（两真人槽恒不同 → 无全局平）
    ///   行动：同地点有存活敌人 → 普攻 1.0（确定性推进至终局）；否则 Move→WasteAP 循环
    /// 客户端脚本时序：判定窗口每轮重复提交（主机窗口一次性接受、重复忽略、超时前自愈）；
    /// 行动窗口「两观察提交」——首次观察到「我的行动者+当前 tick」只武装，下一次脚本轮再提交，
    /// 保证主机已开窗（主机开窗永远晚一拍于状态推进），避免窗口未开即提交被丢。
    /// </summary>
    public class GameLoopNetTests
    {
        private readonly List<NetworkTransport> _transports = new List<NetworkTransport>();

        [TearDown]
        public void TearDown()
        {
            foreach (var t in _transports) t.Stop();
            _transports.Clear();
        }

        // ==================== 输入策略（网络与对照局共用） ====================

        /// <summary>固定判定筹码：按 (turn+playerId)%3 轮换。两真人槽 (0/1) 恒不同 → 全局平不可能。</summary>
        private static ChipAllocation MakeChips(int turn, int playerId)
        {
            var a = new ChipAllocation(playerId);
            switch ((turn + playerId) % 3)
            {
                case 0: a.Set(3, 0, 0); break;
                case 1: a.Set(0, 3, 0); break;
                default: a.Set(0, 0, 3); break;
            }
            return a;
        }

        /// <summary>对照局超时填充等价物（1-1-1，与主机窗口超时 SetDefault 一致）。</summary>
        private static ChipAllocation DefaultChip(int playerId)
        {
            var a = new ChipAllocation(playerId);
            a.SetDefault();
            return a;
        }

        /// <summary>固定行动策略（确定性纯状态函数，网络端与对照局在相同状态点求值 → 决策一致）：
        /// 1) 同地点有存活敌人 → 普攻 1.0（与 AI TryAttack 同语义，保证对局在可控回合内推进至 GameOver）；
        /// 2) 否则未移动过且可达地点可进 → Move(首个连接地点)；
        /// 3) 否则 WasteAP（Move→WasteAP 兜底循环）。</summary>
        private static ReplayActionRecord MakeAction(WorldState state, int actor)
        {
            var rec = new ReplayActionRecord { PlayerId = actor, Kind = ReplayActionKind.WasteAP };
            Character c = state.GetCharacterByPlayer(actor);
            if (c == null || c.IsDead) return rec;
            int enemy = FindSameLocationEnemy(state, actor, c.CurrentLocationId);
            if (enemy >= 0 && c.CanAct && !c.ASC.Tags.HasTag(GameplayTagConstants.GhostForm))
            {
                rec.Kind = ReplayActionKind.Attack;
                rec.IntArg1 = enemy;
                rec.FloatArg = 1f;
                rec.IntArg3 = (int)DamageType.Physical;
                return rec;
            }
            if (c.CanMove && !c.ASC.Tags.HasTag(GameplayTagConstants.Bound))
            {
                int[] conn = state.Locations.GetConnectedLocations(c.CurrentLocationId);
                if (conn != null && conn.Length > 0 && !state.MovedThisTurn[actor])
                {
                    int target = conn[0];
                    if (target != c.CurrentLocationId)
                    {
                        LocationRule rule = state.Locations.Rules[target];
                        if (rule == null || rule.CanEnter(state, c))
                        {
                            rec.Kind = ReplayActionKind.Move;
                            rec.IntArg1 = target;
                        }
                    }
                }
            }
            return rec;
        }

        /// <summary>同地点首个存活敌人（PlayerId 序，确定性；与 AI FindNearestEnemy 同地点优先语义一致）。</summary>
        private static int FindSameLocationEnemy(WorldState state, int self, int locationId)
        {
            for (int i = 0; i < state.PlayerCount; i++)
            {
                if (i == self) continue;
                Character other = state.GetCharacterByPlayer(i);
                if (other != null && !other.IsDead && other.CurrentLocationId == locationId)
                    return i;
            }
            return -1;
        }

        /// <summary>当前可动行动者（与 LocalLockstep.GetNextActor 同语义：ActionOrder 序 + AP>0 或守护充能≥3）。</summary>
        private static int CurrentActor(WorldState s)
        {
            if (s == null || s.TurnManager == null) return -1;
            int[] order = s.TurnManager.ActionOrder;
            if (order == null || order.Length == 0) return -1;
            int start = s.TurnManager.CurrentActorIndex;
            for (int offset = 0; offset < order.Length; offset++)
            {
                int pid = order[(start + offset) % order.Length];
                Character c = s.GetCharacterByPlayer(pid);
                if (c != null && !c.IsDead && (c.ActionPoints > 0 || c.ASC.GetGuardianCharge() >= 3))
                    return pid;
            }
            return -1;
        }

        private static CharacterDef[] BuildDefs(int count)
        {
            var defs = new CharacterDef[count];
            for (int i = 0; i < count; i++) defs[i] = CharacterDefCatalog.Get(i + 1);
            return defs;
        }

        private static SessionConfig MakeConfig(int playerCount, int judgmentMs, int actionMs)
        {
            var defIds = new int[playerCount];
            for (int i = 0; i < playerCount; i++) defIds[i] = i + 1;
            return new SessionConfig
            {
                PlayerCount = playerCount,
                RandomSeed = 42,
                JudgmentWindowMs = judgmentMs,
                ActionWindowMs = actionMs,
                ReconnectGraceMs = 60000,
                HostPlayerDefIds = defIds
            };
        }

        // ==================== 真实 UDP 夹具 ====================

        private sealed class FakeClock
        {
            public long Now;
        }

        /// <summary>真实传输连接对：主机 + 客户端；主机侧记录的 peerId 序列（首个 = 客户端，重连 = 后续）。</summary>
        private sealed class Conn
        {
            public NetworkTransport Host;
            public NetworkTransport Client;
            public readonly List<int> HostSeenPeerIds = new List<int>();
            public bool ClientReady;
        }

        private Conn ConnectPair()
        {
            var c = new Conn();
            c.Host = new NetworkTransport();
            c.Client = new NetworkTransport();
            _transports.Add(c.Host);
            _transports.Add(c.Client);
            c.Host.OnPeerConnected += id => c.HostSeenPeerIds.Add(id);
            c.Client.OnPeerConnected += id => c.ClientReady = true;
            c.Host.StartHost(0, 8, "dynasty-p6");
            Assert.That(c.Client.Connect("127.0.0.1", c.Host.LocalPort, "dynasty-p6", 2000), Is.True);
            int guard = 0;
            while ((!c.ClientReady || c.HostSeenPeerIds.Count == 0) && guard++ < 300)
            {
                c.Host.PollEvents();
                c.Client.PollEvents();
                System.Threading.Thread.Sleep(5);
            }
            Assert.That(c.HostSeenPeerIds.Count, Is.EqualTo(1), "主机应收到客户端连接");
            Assert.That(c.ClientReady, Is.True, "客户端应连接成功");
            return c;
        }

        /// <summary>对局夹具：会话（槽 0 主机真人 + 槽 1 对端真人 + AI 槽）+ 双循环 + 假时钟。</summary>
        private sealed class Game
        {
            public Conn Conn;
            public SessionConfig Config;
            public FakeClock Clock;
            public NetworkSession HostSession;
            public NetworkSession ClientSession;
            public NetworkGameLoop Host;
            public NetworkGameLoop Client;
        }

        private Game NewGame(int playerCount, int judgmentMs, int actionMs)
        {
            var g = new Game
            {
                Config = MakeConfig(playerCount, judgmentMs, actionMs),
                Clock = new FakeClock { Now = 100000 }
            };
            g.Conn = ConnectPair();
            int clientPeerId = g.Conn.HostSeenPeerIds[0];

            g.HostSession = new NetworkSession(g.Config, true);
            Assert.That(g.HostSession.HandleHello(clientPeerId, 1), Is.EqualTo(1), "客户端应入座槽 1");
            Assert.That(g.HostSession.TryStartGame(), Is.True);
            g.ClientSession = new NetworkSession(g.Config, false);
            g.ClientSession.ApplyJoinAccept(1, g.Config.RandomSeed, g.Config.HostPlayerDefIds);

            g.Host = new NetworkGameLoop(g.Conn.Host, g.HostSession, g.Config);
            g.Client = new NetworkGameLoop(g.Conn.Client, g.ClientSession, g.Config);
            g.Host.ClockProvider = () => g.Clock.Now;
            g.Client.ClockProvider = () => g.Clock.Now;
            g.Host.StartGame();   // 广播 GameStart；客户端收到后开局（纯重放模式）
            return g;
        }

        // ==================== 脚本驱动（真人槽输入，网络端） ====================

        /// <summary>
        /// 真人槽脚本：判定每轮重复提交（主机窗口一次性接受、重复忽略、窗口重开自愈——覆盖重判轮）；
        /// 行动「两观察提交」：首次观察到「我的行动者 + 当前 tick」只武装，下一次脚本轮提交——
        /// 主机开窗晚于状态推进一拍，直接提交会撞上窗口未开被丢/行动者不匹配。
        /// SkipChipTurn/SkipActionTurn 用于超时场景（该回合跳过提交 → 主机超时自动 1-1-1 / WasteAP）。
        /// </summary>
        private sealed class ScriptDriver
        {
            public readonly NetworkGameLoop Loop;
            public readonly int Slot;
            public int SkipChipTurn = -1;
            public int SkipActionTurn = -1;

            private bool _actionArmed;
            private int _actionTick = -1;
            private int _lastActionTick = -1;

            public ScriptDriver(NetworkGameLoop loop, int slot)
            {
                Loop = loop;
                Slot = slot;
            }

            public void Run()
            {
                WorldState s = Loop.State;
                if (s == null) return;
                if (s.Phase == TurnPhase.JudgmentPhase)
                {
                    if (s.CurrentTurn != SkipChipTurn)
                        Loop.SubmitChip(Slot, MakeChips(s.CurrentTurn, Slot));
                    return;
                }
                if (s.Phase == TurnPhase.ActionPhase)
                {
                    int actor = CurrentActor(s);
                    if (actor == Slot && s.CurrentTurn != SkipActionTurn)
                    {
                        if (_actionArmed && _actionTick == s.CurrentTick)
                        {
                            Loop.SubmitAction(Slot, MakeAction(s, Slot));
                            _actionArmed = false;
                            _lastActionTick = s.CurrentTick;
                        }
                        else if (!_actionArmed && s.CurrentTick != _lastActionTick)
                        {
                            _actionArmed = true;
                            _actionTick = s.CurrentTick;
                        }
                    }
                    else
                    {
                        _actionArmed = false;
                        _actionTick = -1;
                    }
                }
                else
                {
                    _actionArmed = false;
                    _actionTick = -1;
                }
            }
        }

        // ==================== 纯本地对照局（同 seed/同角色/同输入脚本） ====================

        private sealed class RefResult
        {
            public bool GameOver;
            public int Turns;             // RunTurn 迭代数（含重判轮，仅诊断参考）
            public int FinalTurn;         // 终局时 CurrentTurn（与网络局终局回合比对用）
            public long FinalHash;
            public long[] PerTurnHashes;   // 下标 = 回合号（1-based，回合边界=判定阶段开始）
        }

        /// <summary>
        /// 纯本地 Lockstep 全 AI + 真人槽注入同输入脚本跑完整局。HumanJudgmentInput/ManualInputProvider
        /// 永不返回 null（对照局无网络等待语义，输入即决策）；AI 槽走同 SimpleAI（独立随机流，seed=pid*1000+7）。
        /// defaultChip/defaultAction 为超时场景的输入替换开关：返回 false 的 (turn, playerId) 用
        /// 1-1-1（判定）/ WasteAP（行动）——与网络端超时自动填充语义一致。
        /// </summary>
        private static RefResult RunLocalReference(SessionConfig config, int maxTurns,
            Func<int, int, bool> defaultChip, Func<int, int, bool> defaultAction)
        {
            var state = new WorldState(config.PlayerCount, config.RandomSeed);
            var ais = new SimpleAI[config.PlayerCount];
            for (int i = 0; i < ais.Length; i++) ais[i] = new SimpleAI(i);
            var lockstep = new LocalLockstep(state, ais);
            lockstep.HumanPlayers = new[] { true, true, false, false };
            lockstep.HumanJudgmentInput = i =>
                (defaultChip != null && !defaultChip(state.CurrentTurn, i))
                    ? DefaultChip(i)
                    : MakeChips(state.CurrentTurn, i);
            lockstep.ManualInputProvider = actor =>
            {
                ReplayActionRecord rec = (defaultAction != null && !defaultAction(state.CurrentTurn, actor))
                    ? new ReplayActionRecord { PlayerId = actor, Kind = ReplayActionKind.WasteAP }
                    : MakeAction(state, actor);
                return ReplayActionFactory.Create(rec, state.CurrentTick + 1);
            };
            lockstep.StartGame(BuildDefs(config.PlayerCount));

            var hashes = new long[maxTurns + 2];
            int turns = 0;
            while (state.Phase != TurnPhase.GameOver && turns < maxTurns)
            {
                hashes[state.CurrentTurn] = state.ComputeStateHash();   // 回合边界哈希（判定结算前）
                turns++;
                if (!lockstep.RunTurn()) break;
            }
            return new RefResult
            {
                GameOver = state.Phase == TurnPhase.GameOver,
                Turns = turns,
                FinalTurn = state.CurrentTurn,
                FinalHash = state.ComputeStateHash(),
                PerTurnHashes = hashes
            };
        }

        // ==================== 对局泵送（交替 Tick + 回合边界哈希抽查） ====================

        /// <summary>
        /// 真实 UDP 泵：主机 Tick → 主机脚本 → 客户端 Tick → 客户端脚本（Sleep 让 LiteNetLib 内部
        /// 线程完成回环投递）。每次「两端同处于判定阶段同回合」的回合边界做哈希抽查：
        /// 主机 == 客户端（每回合），每 5 回合对纯本地对照局。
        /// </summary>
        private sealed class GamePump
        {
            public NetworkGameLoop Host;
            public NetworkGameLoop Client;
            public ScriptDriver HostScript;
            public ScriptDriver ClientScript;
            public RefResult Ref;
            public int Rounds;
            private int _lastCheckedTurn;

            /// <summary>泵送一轮；返回主机是否终局。</summary>
            public bool Step()
            {
                Host.Tick();
                HostScript.Run();
                Client.Tick();
                ClientScript.Run();
                System.Threading.Thread.Sleep(1);
                Rounds++;
                CheckBoundary();
                return Host.GameOver;
            }

            private void CheckBoundary()
            {
                if (Host.State == null || Client.State == null) return;
                if (Host.State.Phase != TurnPhase.JudgmentPhase) return;
                if (Client.State.Phase != TurnPhase.JudgmentPhase) return;
                if (Host.State.CurrentTurn != Client.State.CurrentTurn) return;
                int turn = Host.State.CurrentTurn;
                if (turn <= _lastCheckedTurn) return;
                _lastCheckedTurn = turn;
                long h = Host.State.ComputeStateHash();
                Assert.That(Client.State.ComputeStateHash(), Is.EqualTo(h),
                    $"回合 {turn} 边界：主机与客户端哈希失配（防偏移早发现）");
                if (Ref != null && turn % 5 == 0 && turn < Ref.PerTurnHashes.Length && Ref.PerTurnHashes[turn] != 0)
                    Assert.That(h, Is.EqualTo(Ref.PerTurnHashes[turn]),
                        $"回合 {turn} 边界：主机与纯本地对照局哈希失配（网络引入不确定性）");
            }
        }

        /// <summary>泵到终局（回合数上限防挂死）；返回主机终局回合数。终局后补泵让客户端重放完最后一拍。</summary>
        private static void PumpToGameOver(GamePump pump, int maxTurns, out int hostTurns)
        {
            int guard = 0;
            while (!pump.Host.GameOver && guard++ < 60000)
            {
                pump.Step();
                if (pump.Host.State != null && pump.Host.State.CurrentTurn > maxTurns) break;
            }
            Assert.That(pump.Host.GameOver, Is.True,
                $"对局应在 {maxTurns} 回合内终局（当前回合 {pump.Host.State?.CurrentTurn}，轮数 {pump.Rounds}，LastError={pump.Host.LastError}）");
            guard = 0;
            while (!pump.Client.GameOver && guard++ < 500)
                pump.Step();
            Assert.That(pump.Client.GameOver, Is.True,
                $"客户端应重放至终局（Phase={pump.Client.State?.Phase}，Turn={pump.Client.State?.CurrentTurn}，" +
                $"LastError={pump.Client.LastError}，HostPhase={pump.Host.State?.Phase}）");
            hostTurns = pump.Host.State.CurrentTurn;
        }

        /// <summary>终局三端一致性断言：客户端 == 主机 == 对照局，且对照局终局回合数一致。
        /// 主机 LastError 白名单：I1 槽位持有者校验失败（旧连接断线前在途的过期提交被拒）与
        /// I3 判定提交轮次不匹配（客户端落后整回合时的过期筹码被拒）均为防御性正确行为，
        /// 游戏一致性不受影响；白名单之外的任何主机错误都是缺陷。</summary>
        private static void AssertEndHashes(GamePump pump, RefResult refResult, int hostTurns)
        {
            long hostHash = pump.Host.State.ComputeStateHash();
            Assert.That(pump.Client.State.ComputeStateHash(), Is.EqualTo(hostHash), "终局哈希：客户端 == 主机");
            Assert.That(refResult.FinalHash, Is.EqualTo(hostHash), "终局哈希：主机 == 纯本地对照局（网络无不确定性）");
            Assert.That(refResult.FinalTurn, Is.EqualTo(hostTurns), "对照局与网络局终局回合数一致");
            if (pump.Host.LastError != null)
                Assert.That(pump.Host.LastError, Does.Match("槽位持有者校验失败|判定提交轮次不匹配"),
                    $"主机只允许防御性拒绝（过期/冒名提交），其余错误为缺陷: {pump.Host.LastError}");
            Assert.That(pump.Client.LastError, Is.Null, "客户端全程不得报告错误");
        }

        // ==================== 场景 1 + 4：完整对局一致 + 每 5 回合哈希抽查 ====================

        [Test]
        [Timeout(180000)]
        public void 完整对局_真实UDP_终局哈希三端一致_每5回合抽查()
        {
            var g = NewGame(4, 500, 500);
            RefResult refResult = RunLocalReference(g.Config, 200, null, null);
            Assert.That(refResult.GameOver, Is.True, $"对照局应在 200 回合内终局（实际回合数 {refResult.Turns}）");

            var pump = new GamePump
            {
                Host = g.Host,
                Client = g.Client,
                HostScript = new ScriptDriver(g.Host, 0),
                ClientScript = new ScriptDriver(g.Client, 1),
                Ref = refResult
            };
            PumpToGameOver(pump, 200, out int hostTurns);
            AssertEndHashes(pump, refResult, hostTurns);
        }

        // ==================== 场景 2：中途重连追平一致 ====================

        [Test]
        [Timeout(180000)]
        public void 中途重连_真实UDP_追平后继续至终局哈希一致()
        {
            var g = NewGame(4, 500, 500);
            RefResult refResult = RunLocalReference(g.Config, 200, null, null);
            Assert.That(refResult.GameOver, Is.True, $"对照局应在 200 回合内终局（实际回合数 {refResult.Turns}）");
            var hostScript = new ScriptDriver(g.Host, 0);
            var clientScript = new ScriptDriver(g.Client, 1);

            // —— 推进到对局中段（回合 5 判定边界，主/客户端同处边界——客户端重放落后一拍属正常，须等追平）
            int guard = 0;
            while ((g.Host.State == null || g.Host.State.CurrentTurn < 5 || g.Host.State.Phase != TurnPhase.JudgmentPhase
                    || g.Client.State == null || g.Client.State.CurrentTurn < 5 || g.Client.State.Phase != TurnPhase.JudgmentPhase)
                   && guard++ < 20000)
            {
                g.Host.Tick();
                hostScript.Run();
                g.Client.Tick();
                clientScript.Run();
                System.Threading.Thread.Sleep(1);
            }
            Assert.That(g.Host.State.CurrentTurn, Is.EqualTo(5), "应推进到回合 5 判定边界");
            Assert.That(g.Host.State.Phase, Is.EqualTo(TurnPhase.JudgmentPhase));
            Assert.That(g.Host.State.ComputeStateHash(), Is.EqualTo(g.Client.State.ComputeStateHash()),
                "前置：断线前两端状态一致");

            // —— 断线：客户端记录断线锚点并 Stop；主机释放槽 1 并冻结（宽限期等待重连）
            int oldPeerId = g.Conn.HostSeenPeerIds[0];
            g.Client.NotifyDisconnected();
            g.Conn.Client.Stop();
            Assert.That(g.HostSession.HandleDisconnect(oldPeerId), Is.EqualTo(1), "主机应释放槽 1");
            Assert.That(g.Host.IsSlotFrozen(1), Is.True, "断线槽 1 应冻结（宽限期不超时填充）");

            // —— 重连：新传输连接主机 → 新会话/新循环 → 断线锚点 0（全量重放）→ SendResyncRequest
            var ct2 = new NetworkTransport();
            _transports.Add(ct2);
            bool c2Ready = false;
            ct2.OnPeerConnected += id => c2Ready = true;
            Assert.That(ct2.Connect("127.0.0.1", g.Conn.Host.LocalPort, g.Config.ConnectKey, 2000), Is.True);
            guard = 0;
            while ((!c2Ready || g.Conn.HostSeenPeerIds.Count < 2) && guard++ < 300)
            {
                g.Conn.Host.PollEvents();
                ct2.PollEvents();
                System.Threading.Thread.Sleep(5);
            }
            Assert.That(c2Ready, Is.True, "重连客户端应连接成功");
            int newPeerId = g.Conn.HostSeenPeerIds[1];

            var session2 = new NetworkSession(g.Config, false);
            session2.ApplyJoinAccept(1, g.Config.RandomSeed, g.Config.HostPlayerDefIds);
            var client2 = new NetworkGameLoop(ct2, session2, g.Config);
            client2.ClockProvider = () => g.Clock.Now;
            client2.NotifyDisconnected();   // 全新会话无状态 → 锚点 (0,0) → 全量重放
            client2.SendResyncRequest();    // 经真实 UDP 发起重连
            var client2Script = new ScriptDriver(client2, 1);

            // —— 追赶：先等初始 ResyncReply 到达（IsResyncing 变 true），再等追平完成。
            // 追赶期间不跑客户端2 脚本——重放中的提交会污染主机窗口（客户端2 状态未就绪前脚本亦自然跳过）。
            guard = 0;
            while (!client2.Reconnect.IsResyncing && guard++ < 1000)
            {
                g.Host.Tick();
                hostScript.Run();
                client2.Tick();
                System.Threading.Thread.Sleep(1);
            }
            Assert.That(client2.Reconnect.IsResyncing, Is.True, "应收到初始 ResyncReply 进入追平（真实 UDP 重连请求往返）");
            guard = 0;
            while (client2.Reconnect.IsResyncing && guard++ < 4000)
            {
                g.Host.Tick();
                hostScript.Run();
                client2.Tick();
                System.Threading.Thread.Sleep(1);
            }
            Assert.That(client2.Reconnect.IsResyncing, Is.False, "重放应在有限步内追平");
            Assert.That(client2.LastError, Is.Null, "追平期间不得进入错误状态");
            Assert.That(g.Host.IsSlotFrozen(1), Is.False, "重连成功后槽 1 应解冻");
            Assert.That(g.HostSession.PlayerPeerIds[1], Is.EqualTo(newPeerId), "槽 1 应由重连 peer 持有");
            Assert.That(client2.State.ComputeStateHash(), Is.EqualTo(g.Host.State.ComputeStateHash()),
                "追平后哈希必须与主机一致（快照+日志重放确定性）");

            // —— 追平后继续至终局（客户端2 脚本接管槽 1）——终局哈希三端一致
            var pump = new GamePump
            {
                Host = g.Host,
                Client = client2,
                HostScript = hostScript,
                ClientScript = client2Script,
                Ref = refResult
            };
            PumpToGameOver(pump, 200, out int hostTurns);
            AssertEndHashes(pump, refResult, hostTurns);
        }

        // ==================== 场景 3：窗口超时路径（真实 UDP + 假时钟快进） ====================

        [Test]
        [Timeout(120000)]
        public void 窗口超时_真实UDP_判定行动超时路径跑通()
        {
            var g = NewGame(4, 300, 300);
            // 对照局镜像：槽 1 回合 2 判定用 1-1-1（超时填充等价）、回合 3 行动全 WasteAP（超时跳过等价）
            RefResult refResult = RunLocalReference(g.Config, 200,
                (turn, pid) => !(pid == 1 && turn == 2),
                (turn, pid) => !(pid == 1 && turn == 3));
            Assert.That(refResult.GameOver, Is.True, $"对照局应在 200 回合内终局（实际回合数 {refResult.Turns}）");

            var hostScript = new ScriptDriver(g.Host, 0);
            var clientScript = new ScriptDriver(g.Client, 1) { SkipChipTurn = 2, SkipActionTurn = 3 };
            bool sawChipTimeout = false;
            bool sawActionTimeout = false;

            // —— 阶段 A：超时场景（客户端跳过提交 → 每 20 轮推进假时钟触发窗口超时，重判/重开窗被后续轮覆盖）
            int guard = 0;
            while (guard++ < 30000 && (g.Host.State == null || g.Host.State.CurrentTurn < 4))
            {
                g.Host.Tick();
                hostScript.Run();
                g.Client.Tick();
                clientScript.Run();
                System.Threading.Thread.Sleep(1);

                if (g.Host.State != null && g.Host.State.Phase == TurnPhase.JudgmentPhase
                    && g.Host.State.CurrentTurn == 2 && guard % 20 == 0)
                {
                    g.Clock.Now += g.Config.JudgmentWindowMs + 1;   // 槽 1 未提交 → 自动 1-1-1
                    sawChipTimeout = true;
                }
                if (g.Host.State != null && g.Host.State.Phase == TurnPhase.ActionPhase
                    && g.Host.State.CurrentTurn == 3 && CurrentActor(g.Host.State) == 1 && guard % 20 == 0)
                {
                    g.Clock.Now += g.Config.ActionWindowMs + 1;     // 槽 1 未提交 → 自动 WasteAP（isPass=1）
                    sawActionTimeout = true;
                }
            }
            Assert.That(sawChipTimeout, Is.True, "回合 2 判定窗口应触发超时");
            Assert.That(sawActionTimeout, Is.True, "回合 3 行动窗口应触发超时");
            Assert.That(g.Host.State.CurrentTurn, Is.GreaterThanOrEqualTo(4), "超时路径下对局应越过回合 4");
            // 注：此处不做主机/客户端哈希对比——真实 UDP 下客户端纯重放落后主机若干批次属正常，
            // 非静止点比较会误报；一致性由阶段 B 的 GamePump 回合边界抽查 + 终局三端哈希断言覆盖。

            // —— 阶段 B：脚本恢复（回合 4+ 正常提交）→ 泵至终局 → 三端哈希一致
            var pump = new GamePump
            {
                Host = g.Host,
                Client = g.Client,
                HostScript = hostScript,
                ClientScript = clientScript,
                Ref = refResult
            };
            PumpToGameOver(pump, 200, out int hostTurns);
            AssertEndHashes(pump, refResult, hostTurns);
        }
    }
}

