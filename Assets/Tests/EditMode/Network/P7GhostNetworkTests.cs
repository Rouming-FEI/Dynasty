using System;
using System.Collections.Generic;
using AI;
using Core;
using Core.GAS;
using Core.Replay;
using Gameplay.Characters;
using Gameplay.Judgment;
using Network.Local;
using Network.Session;
using Network.Transport;
using NUnit.Framework;

namespace Dynasty.Tests.Network
{
    /// <summary>
    /// P7 网络一致性：slot0=数据幽灵(16)、slot1=混沌法师(14)、slot2=双子(15)、slot3=守护者(1)，
    /// 经真实 UDP 双端跑 ≥6 回合（覆盖 Harmonics 造分身 → 分身活跃期 → 回合末扣减 → 分身死亡判定全时段），
    /// 每个回合边界（判定阶段开始）与终局断言主机哈希 == 客户端哈希，且与纯本地同 seed 同角色对照局一致。
    /// 输入同源：判定/行动均为纯状态函数（MakeChips/MakeAction），AI 槽（2/3）独立随机流
    /// （seed=playerId*1000+7）与对照局完全一致——网络只传递输入，不引入不确定性。
    /// 覆盖保证：seed=49 经本地预扫描固化——分身活跃期与非 V1 形态（受击解锁 + Handsonic 切换）
    /// 均在前 7 回合内出现；网络端在比对回合断言 sawCloneTurn/sawSwitchTurn，对照局同步自检，
    /// 防「分身/形态从未发生」的假绿。
    /// </summary>
    public class P7GhostNetworkTests
    {
        private readonly List<NetworkTransport> _transports = new List<NetworkTransport>();

        [TearDown]
        public void TearDown()
        {
            foreach (var t in _transports) t.Stop();
            _transports.Clear();
        }

        // ---- 固定输入（纯状态函数，双端/对照局一致）----

        private static ChipAllocation MakeChips(int turn, int playerId)
        {
            var a = new ChipAllocation(playerId);
            // 两真人槽 (0/1) 恒不同手型 → 无全局平
            switch ((turn + playerId) % 3)
            {
                case 0: a.Set(3, 0, 0); break;
                case 1: a.Set(0, 3, 0); break;
                default: a.Set(0, 0, 3); break;
            }
            return a;
        }

        /// <summary>行动决策（纯函数）：数据幽灵先造分身；分身后每回合优先切换形态
        /// （Handsonic 125 已解锁高形态时，FormSwitched 门控 1 次/回合——保证哈希局内
        /// 形态标签真实变化）；有同地点敌人普攻（形态伤害）；混沌法师对敌人放模拟枪
        /// （免地理）；否则移动/浪费。</summary>
        private static ReplayActionRecord MakeAction(WorldState s, int actor)
        {
            var rec = new ReplayActionRecord { PlayerId = actor, Kind = ReplayActionKind.WasteAP };
            Character c = s.GetCharacterByPlayer(actor);
            if (c == null || c.IsDead) return rec;
            if (c.Definition?.Id == SkillSystem.DataGhostCharacterId)
            {
                if (!s.GhostCloneActive && c.ActionPoints >= 1)
                {
                    rec.Kind = ReplayActionKind.UseSkill;
                    rec.IntArg1 = 1; // Harmonics（DataGhostDef.Skills[1]）
                    rec.IntArg2 = actor;
                    return rec;
                }
                // 已受击解锁更高形态且本回合未切 → Handsonic（DataGhostDef.Skills[0]，0 AP）
                if (!c.ASC.Tags.HasTag(GameplayTagConstants.FormSwitched)
                    && (c.ASC.Tags.HasTag(GameplayTagConstants.V2Unlocked)
                        || c.ASC.Tags.HasTag(GameplayTagConstants.V3Unlocked)
                        || c.ASC.Tags.HasTag(GameplayTagConstants.V4Unlocked)
                        || c.ASC.Tags.HasTag(GameplayTagConstants.V5Unlocked)))
                {
                    rec.Kind = ReplayActionKind.UseSkill;
                    rec.IntArg1 = 0; // Handsonic 形态切换
                    rec.IntArg2 = actor;
                    return rec;
                }
            }
            int enemy = -1;
            int sameLoc = -1;
            for (int i = 0; i < s.PlayerCount; i++)
            {
                Character o = s.GetCharacterByPlayer(i);
                if (o == null || o.IsDead || o.PlayerId == actor) continue;
                if (enemy < 0) enemy = i;
                if (o.CurrentLocationId == c.CurrentLocationId) sameLoc = i;
            }
            if (c.Definition?.Id == SkillSystem.ChaosMageCharacterId && enemy >= 0)
            {
                rec.Kind = ReplayActionKind.ChaosSimulate;
                rec.IntArg1 = 3;          // 模拟枪（远程 2）
                rec.IntArg2 = enemy;
                return rec;
            }
            if (sameLoc >= 0 && c.CanAct && c.ActionPoints >= 1)
            {
                rec.Kind = ReplayActionKind.Attack;
                rec.IntArg1 = sameLoc;
                rec.FloatArg = 1f;
                rec.IntArg3 = (int)DamageType.Physical;
                return rec;
            }
            return rec; // WasteAP（推进回合，分身每回合末扣减依赖回合推进）
        }

        // ---- 夹具 ----

        private sealed class FakeClock
        {
            public long Now;
        }

        private sealed class Conn
        {
            public NetworkTransport Host;
            public NetworkTransport Client;
            public readonly List<int> HostSeenPeerIds = new List<int>();
            public bool ClientReady;
        }

        private Conn ConnectPair()
        {
            var c = new Conn { Host = new NetworkTransport(), Client = new NetworkTransport() };
            _transports.Add(c.Host);
            _transports.Add(c.Client);
            c.Host.OnPeerConnected += id => c.HostSeenPeerIds.Add(id);
            c.Client.OnPeerConnected += id => c.ClientReady = true;
            c.Host.StartHost(0, 8, "dynasty-p7");
            Assert.That(c.Client.Connect("127.0.0.1", c.Host.LocalPort, "dynasty-p7", 2000), Is.True);
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

        private sealed class ScriptDriver
        {
            public readonly NetworkGameLoop Loop;
            public readonly int Slot;
            private bool _armed;
            private int _armedTick = -1;
            private int _lastActionTick = -1;

            public ScriptDriver(NetworkGameLoop loop, int slot) { Loop = loop; Slot = slot; }

            public void Run()
            {
                WorldState s = Loop.State;
                if (s == null) return;
                if (s.Phase == TurnPhase.JudgmentPhase)
                {
                    Loop.SubmitChip(Slot, MakeChips(s.CurrentTurn, Slot));
                    return;
                }
                if (s.Phase != TurnPhase.ActionPhase) return;
                int actor = CurrentActor(s);
                if (actor != Slot)
                {
                    _armed = false;
                    _armedTick = -1;
                    return;
                }
                if (_armed && _armedTick == s.CurrentTick)
                {
                    Loop.SubmitAction(Slot, MakeAction(s, Slot)); // 两观察提交：本拍提交
                    _armed = false;
                    _lastActionTick = s.CurrentTick;
                }
                else if (!_armed && s.CurrentTick != _lastActionTick)
                {
                    _armed = true;   // 首次观察：武装，下一拍提交
                    _armedTick = s.CurrentTick;
                }
            }
        }

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
                if (c != null && !c.IsDead && (c.ActionPoints > 0 || c.ASC.GetGuardianCharge() >= 3)) return pid;
            }
            return -1;
        }

        // ---- 纯本地对照局（同 seed/同角色表/同输入函数；AI 槽独立随机流同源）----

        private sealed class RefResult
        {
            public long[] PerTurnHashes;    // 下标 = 回合号（1-based，回合边界=判定阶段开始）
            public int FinalTurn;           // 中止时 CurrentTurn
            public long FinalHash;
            public bool SawCloneAtBoundary;  // 对照局是否也出现过「分身活跃的回合边界」
            public bool SawSwitchAtBoundary; // 对照局是否也出现过「幽灵非 V1 形态的回合边界」
        }

        private static RefResult RunLocalReference(SessionConfig config, int maxTurns)
        {
            var state = new WorldState(config.PlayerCount, config.RandomSeed);
            var ais = new SimpleAI[config.PlayerCount];
            for (int i = 0; i < ais.Length; i++) ais[i] = new SimpleAI(i);
            var lockstep = new LocalLockstep(state, ais);
            lockstep.HumanPlayers = new[] { true, true, false, false };
            lockstep.HumanJudgmentInput = i => MakeChips(state.CurrentTurn, i);
            lockstep.ManualInputProvider = actor =>
            {
                return ReplayActionFactory.Create(MakeAction(state, actor), state.CurrentTick + 1);
            };
            var defs = new CharacterDef[config.PlayerCount];
            for (int i = 0; i < config.PlayerCount; i++)
                defs[i] = CharacterDefCatalog.Get(config.HostPlayerDefIds[i]);
            lockstep.StartGame(defs);

            var hashes = new long[maxTurns + 2];
            bool sawClone = false;
            bool sawSwitch = false;   // 对照局里幽灵曾以非 V1 形态处在回合边界（输入同源自检）
            int turns = 0;
            while (state.Phase != TurnPhase.GameOver && turns < maxTurns)
            {
                hashes[state.CurrentTurn] = state.ComputeStateHash();   // 回合边界哈希（判定结算前）
                if (state.GhostCloneActive) sawClone = true;
                Character g = state.GetCharacterByPlayer(0);
                if (g != null && !g.IsDead && DataGhostFormAttack.CurrentForm(g) != 1) sawSwitch = true;
                turns++;
                if (!lockstep.RunTurn()) break;
            }
            return new RefResult
            {
                PerTurnHashes = hashes,
                FinalTurn = state.CurrentTurn,
                FinalHash = state.ComputeStateHash(),
                SawCloneAtBoundary = sawClone,
                SawSwitchAtBoundary = sawSwitch
            };
        }

        // ---- 网络对局 ----

        [Test]
        [Timeout(120000)]
        public void 含数据幽灵分身局_真实UDP_双端与对照局回合边界哈希一致()
        {
            var config = new SessionConfig
            {
                PlayerCount = 4,
                // seed 49：本地预扫描命中「分身活跃 + 形态切换均出现在前 7 回合」（探针实测固化，
                // 见类注释；seed=42 分身活跃可中但幽灵解锁形态偏晚，可能拖出比对窗口）
                RandomSeed = 49,
                JudgmentWindowMs = 400,
                ActionWindowMs = 400,
                ReconnectGraceMs = 60000,
                HostPlayerDefIds = new[] { 16, 14, 15, 1 }
            };
            var clock = new FakeClock { Now = 100000 };
            var conn = ConnectPair();
            int clientPeerId = conn.HostSeenPeerIds[0];

            var hostSession = new NetworkSession(config, true);
            Assert.That(hostSession.HandleHello(clientPeerId, 1), Is.EqualTo(1), "客户端入座槽 1");
            Assert.That(hostSession.TryStartGame(), Is.True);
            var clientSession = new NetworkSession(config, false);
            clientSession.ApplyJoinAccept(1, config.RandomSeed, config.HostPlayerDefIds);

            var host = new NetworkGameLoop(conn.Host, hostSession, config);
            var client = new NetworkGameLoop(conn.Client, clientSession, config);
            host.ClockProvider = () => clock.Now;
            client.ClockProvider = () => clock.Now;
            host.StartGame();

            // 本地对照局：同 seed/同角色表/同输入函数，跑 10 回合上限
            RefResult refResult = RunLocalReference(config, 10);
            Assert.That(refResult.PerTurnHashes[1], Is.Not.Zero, "对照局应至少完成回合 1");

            var hostScript = new ScriptDriver(host, 0);
            var clientScript = new ScriptDriver(client, 1);

            int lastCheckedTurn = 0;
            bool sawCloneTurn = false;  // 至少在一个已比对回合里分身处于活跃期
            bool sawSwitchTurn = false; // 至少在一个已比对回合里幽灵处于非 V1 形态（形态标签变化）
            int guard = 0;
            while (guard++ < 20000)
            {
                host.Tick();
                hostScript.Run();
                client.Tick();
                clientScript.Run();
                System.Threading.Thread.Sleep(1);

                if (host.State != null && client.State != null
                    && host.State.Phase == TurnPhase.JudgmentPhase
                    && client.State.Phase == TurnPhase.JudgmentPhase
                    && host.State.CurrentTurn == client.State.CurrentTurn
                    && host.State.CurrentTurn > lastCheckedTurn)
                {
                    lastCheckedTurn = host.State.CurrentTurn;
                    long h = host.State.ComputeStateHash();
                    Assert.That(client.State.ComputeStateHash(), Is.EqualTo(h),
                        $"回合 {lastCheckedTurn} 边界：双端哈希失配");
                    // 对照局三端比对（若对照局已推进到该回合；GameOver 提前则后续回合无可比）
                    if (lastCheckedTurn < refResult.PerTurnHashes.Length && refResult.PerTurnHashes[lastCheckedTurn] != 0)
                        Assert.That(h, Is.EqualTo(refResult.PerTurnHashes[lastCheckedTurn]),
                            $"回合 {lastCheckedTurn} 边界：主机与纯本地对照局哈希失配（网络引入不确定性）");
                    if (host.State.GhostCloneActive) sawCloneTurn = true;
                    Character g0 = host.State.GetCharacterByPlayer(0);
                    if (g0 != null && !g0.IsDead && DataGhostFormAttack.CurrentForm(g0) != 1) sawSwitchTurn = true;
                }
                if (lastCheckedTurn >= 7) break;
            }
            Assert.That(lastCheckedTurn, Is.GreaterThanOrEqualTo(7), "对局应推进 ≥7 回合");
            Assert.That(sawCloneTurn, Is.True, "分身活跃期应至少出现在一个比对回合（覆盖分身字段哈希）");
            Assert.That(sawSwitchTurn, Is.True,
                "幽灵形态切换应至少出现在一个比对回合（覆盖形态标签进哈希的真实变化）");
            Assert.That(refResult.SawCloneAtBoundary, Is.True,
                "对照局也应出现分身活跃的回合边界（输入同源性自检）");
            Assert.That(refResult.SawSwitchAtBoundary, Is.True,
                "对照局也应出现幽灵非 V1 形态的回合边界（输入同源性自检）");
            // 主机 LastError 白名单：I1 槽位持有者校验失败 / I3 判定提交轮次不匹配
            // （客户端重放落后整回合时的过期提交被拒）均为防御性正确行为（P6 同款）；
            // 白名单之外的任何主机错误都是缺陷。
            if (host.LastError != null)
                Assert.That(host.LastError,
                    Does.Match("槽位持有者校验失败|判定提交轮次不匹配"),
                    $"主机只允许防御性拒绝（过期/冒名提交），其余错误为缺陷: {host.LastError}");
            Assert.That(client.LastError, Is.Null, $"客户端不得报错：{client.LastError}");
            Assert.That(client.State.ComputeStateHash(), Is.EqualTo(host.State.ComputeStateHash()), "终局双端哈希一致");
        }
    }
}
