using System;
using Network.Session;
using NUnit.Framework;

namespace Dynasty.Tests.Network
{
    /// <summary>会话状态机测试：纯状态机（无真实 UDP）——主机槽位分配/满员拒绝/断线释放/
    /// TryStartGame 开战、客户端 ApplyJoinAccept 应用加入结果。</summary>
    public class SessionTests
    {
        private static SessionConfig HostConfig(int playerCount = 4)
        {
            var defIds = new int[playerCount];
            for (int i = 0; i < playerCount; i++)
                defIds[i] = i + 1;
            return new SessionConfig { PlayerCount = playerCount, HostPlayerDefIds = defIds };
        }

        [Test]
        public void HandleHello_四人对局分配0到3号槽位()
        {
            var session = new NetworkSession(HostConfig(), true);

            Assert.That(session.Phase, Is.EqualTo(SessionPhase.Lobby));
            Assert.That(session.LocalPlayerId, Is.EqualTo(0));   // 主机固定 = 0
            Assert.That(session.ConnectedPlayerCount, Is.EqualTo(0));
            Assert.That(session.PlayerPeerIds, Is.EqualTo(new[] { -1, -1, -1, -1 }));

            int joined = 0;
            session.OnPlayerJoined += pid => { Assert.That(pid, Is.EqualTo(joined)); joined++; };

            Assert.That(session.HandleHello(1, 1), Is.EqualTo(0));
            Assert.That(session.HandleHello(2, 2), Is.EqualTo(1));
            Assert.That(session.HandleHello(3, 3), Is.EqualTo(2));
            Assert.That(session.HandleHello(4, 4), Is.EqualTo(3));

            Assert.That(joined, Is.EqualTo(4));                     // OnPlayerJoined 依序触发 0-3
            Assert.That(session.ConnectedPlayerCount, Is.EqualTo(4));
            Assert.That(session.PlayerPeerIds, Is.EqualTo(new[] { 1, 2, 3, 4 }));
            Assert.That(session.Seed, Is.EqualTo(42));              // 继承 SessionConfig.RandomSeed
            Assert.That(session.PlayerDefIds, Is.EqualTo(new[] { 1, 2, 3, 4 }));
        }

        [Test]
        public void HandleHello_满员拒绝返回负一()
        {
            var session = new NetworkSession(HostConfig(), true);
            session.HandleHello(1, 1);
            session.HandleHello(2, 2);
            session.HandleHello(3, 3);
            session.HandleHello(4, 4);

            Assert.That(session.HandleHello(5, 1), Is.EqualTo(-1)); // 第 5 人拒绝
            Assert.That(session.ConnectedPlayerCount, Is.EqualTo(4));
            Assert.That(session.PlayerPeerIds, Is.EqualTo(new[] { 1, 2, 3, 4 }));
        }

        [Test]
        public void HandleDisconnect_释放槽位并返回玩家id()
        {
            var session = new NetworkSession(HostConfig(), true);
            session.HandleHello(1, 1);
            session.HandleHello(2, 2);
            session.HandleHello(3, 3);
            session.HandleHello(4, 4);
            int left = -1;
            session.OnPlayerLeft += pid => left = pid;

            Assert.That(session.HandleDisconnect(2), Is.EqualTo(1)); // peerId 2 持有槽 1
            Assert.That(left, Is.EqualTo(1));
            Assert.That(session.PlayerPeerIds[1], Is.EqualTo(-1));
            Assert.That(session.ConnectedPlayerCount, Is.EqualTo(3));
            Assert.That(session.HandleDisconnect(99), Is.EqualTo(-1)); // 未知 peer 无槽位

            // 断线释放后槽位可复用（新对端拿到同 playerId）
            Assert.That(session.HandleHello(9, 9), Is.EqualTo(1));
            Assert.That(session.ConnectedPlayerCount, Is.EqualTo(4));
        }

        [Test]
        public void TryStartGame_满员返回true并切换InGame()
        {
            var session = new NetworkSession(HostConfig(), true);
            bool allReady = false;
            session.OnAllReady += () => allReady = true;

            Assert.That(session.TryStartGame(), Is.False);          // 未满员不可开战
            session.HandleHello(1, 1);
            session.HandleHello(2, 2);
            session.HandleHello(3, 3);
            session.HandleHello(4, 4);

            Assert.That(allReady, Is.True);                         // 满员瞬间触发开战信号
            Assert.That(session.TryStartGame(), Is.True);
            Assert.That(session.Phase, Is.EqualTo(SessionPhase.InGame));
            Assert.That(session.TryStartGame(), Is.False);          // 已 InGame 不可重复开战
        }

        [Test]
        public void ApplyJoinAccept_客户端应用加入结果并切InGame()
        {
            var session = new NetworkSession(HostConfig(), false, -1);
            Assert.That(session.Phase, Is.EqualTo(SessionPhase.Lobby));
            Assert.That(session.LocalPlayerId, Is.EqualTo(-1));     // 加入前未知
            Assert.That(session.PlayerDefIds, Is.Null);             // 角色表等 JoinAccept 下发

            session.ApplyJoinAccept(3, 123, new[] { 5, 6, 7, 8 });

            Assert.That(session.LocalPlayerId, Is.EqualTo(3));
            Assert.That(session.Seed, Is.EqualTo(123));             // 以主机下发的种子为准
            Assert.That(session.PlayerDefIds, Is.EqualTo(new[] { 5, 6, 7, 8 }));
            Assert.That(session.Phase, Is.EqualTo(SessionPhase.InGame));
        }

        [Test]
        public void ApplyJoinAccept_角色表长度不符抛异常()
        {
            var session = new NetworkSession(HostConfig(), false);

            Assert.Throws<ArgumentException>(() => session.ApplyJoinAccept(1, 1, new[] { 1, 2, 3 }));
            Assert.Throws<ArgumentException>(() => session.ApplyJoinAccept(1, 1, null));
            Assert.That(session.Phase, Is.EqualTo(SessionPhase.Lobby));   // 失败不破坏状态
            Assert.That(session.LocalPlayerId, Is.EqualTo(-1));
        }
    }
}
