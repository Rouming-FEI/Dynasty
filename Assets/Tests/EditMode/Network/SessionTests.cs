using System;
using Network.Session;
using NUnit.Framework;

namespace Dynasty.Tests.Network
{
    /// <summary>会话状态机测试：纯状态机（无真实 UDP）——槽位模型（槽 0=主机、对端从 1 起）、
    /// 满员拒绝/断线释放/TryStartGame 手动开战、客户端 ApplyJoinAccept 应用加入结果、防御校验。</summary>
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
        public void HandleHello_对端从槽1起分配_满员触发OnAllReady()
        {
            var session = new NetworkSession(HostConfig(), true);

            Assert.That(session.Phase, Is.EqualTo(SessionPhase.Lobby));
            Assert.That(session.LocalPlayerId, Is.EqualTo(0));   // 主机固定占槽 0
            Assert.That(session.ConnectedPlayerCount, Is.EqualTo(0));
            Assert.That(session.PlayerPeerIds, Is.EqualTo(new[] { -1, -1, -1, -1 }));

            int joined = 0;
            bool allReady = false;
            session.OnPlayerJoined += pid => { Assert.That(pid, Is.EqualTo(joined + 1)); joined++; };
            session.OnAllReady += () => allReady = true;

            Assert.That(session.HandleHello(1, 1), Is.EqualTo(1));
            Assert.That(session.HandleHello(2, 2), Is.EqualTo(2));
            Assert.That(session.HandleHello(3, 3), Is.EqualTo(3));

            Assert.That(joined, Is.EqualTo(3));                     // OnPlayerJoined 依序触发 1-3
            Assert.That(allReady, Is.True);                         // 对端槽满（PlayerCount-1）触发满员信号
            Assert.That(session.ConnectedPlayerCount, Is.EqualTo(3));
            Assert.That(session.PlayerPeerIds, Is.EqualTo(new[] { -1, 1, 2, 3 }));   // 槽 0 恒 -1
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

            Assert.That(session.HandleHello(4, 4), Is.EqualTo(-1)); // 第 4 个对端无空槽（PlayerCount=4 对端槽仅 1..3）
            Assert.That(session.ConnectedPlayerCount, Is.EqualTo(3));
            Assert.That(session.PlayerPeerIds, Is.EqualTo(new[] { -1, 1, 2, 3 }));
        }

        [Test]
        public void HandleHello_重复peerId返回原playerId不重复占槽()
        {
            var session = new NetworkSession(HostConfig(), true);
            Assert.That(session.HandleHello(7, 1), Is.EqualTo(1));

            Assert.That(session.HandleHello(7, 9), Is.EqualTo(1));  // 二次 Hello 拿回原 playerId
            Assert.That(session.HandleHello(8, 3), Is.EqualTo(2));  // 新 peer 继续分配下一槽
            Assert.That(session.ConnectedPlayerCount, Is.EqualTo(2));   // 槽数不变
            Assert.That(session.PlayerPeerIds, Is.EqualTo(new[] { -1, 7, 8, -1 }));
        }

        [Test]
        public void HandleDisconnect_释放槽位并返回玩家id()
        {
            var session = new NetworkSession(HostConfig(), true);
            session.HandleHello(1, 1);
            session.HandleHello(2, 2);
            session.HandleHello(3, 3);
            int left = -1;
            session.OnPlayerLeft += pid => left = pid;

            Assert.That(session.HandleDisconnect(2), Is.EqualTo(2)); // peerId 2 持有槽 2
            Assert.That(left, Is.EqualTo(2));
            Assert.That(session.PlayerPeerIds[2], Is.EqualTo(-1));
            Assert.That(session.ConnectedPlayerCount, Is.EqualTo(2));
            Assert.That(session.HandleDisconnect(99), Is.EqualTo(-1)); // 未知 peer 无槽位

            // 断线释放后槽位可复用（新对端拿到同 playerId）
            Assert.That(session.HandleHello(9, 9), Is.EqualTo(2));
            Assert.That(session.ConnectedPlayerCount, Is.EqualTo(3));
        }

        [Test]
        public void TryStartGame_主机手动开战切InGame()
        {
            var session = new NetworkSession(HostConfig(), true);

            Assert.That(session.TryStartGame(), Is.True);           // 无需满员，Lobby 即可手动开战
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

        [Test]
        public void ApplyJoinAccept_playerId越界抛异常()
        {
            var session = new NetworkSession(HostConfig(), false);

            Assert.Throws<ArgumentException>(() => session.ApplyJoinAccept(-1, 1, new[] { 1, 2, 3, 4 }));
            Assert.Throws<ArgumentException>(() => session.ApplyJoinAccept(4, 1, new[] { 1, 2, 3, 4 }));
            Assert.That(session.Phase, Is.EqualTo(SessionPhase.Lobby));   // 失败不破坏状态
            Assert.That(session.LocalPlayerId, Is.EqualTo(-1));
        }

        [Test]
        public void 构造函数_参数校验()
        {
            Assert.Throws<ArgumentNullException>(() => new NetworkSession(null, true));

            var badCount = new SessionConfig { PlayerCount = 0, HostPlayerDefIds = new int[0] };
            Assert.Throws<ArgumentException>(() => new NetworkSession(badCount, true));

            var noDefs = new SessionConfig { PlayerCount = 4, HostPlayerDefIds = null };
            Assert.Throws<ArgumentException>(() => new NetworkSession(noDefs, true));

            var shortDefs = new SessionConfig { PlayerCount = 4, HostPlayerDefIds = new[] { 1, 2, 3 } };
            Assert.Throws<ArgumentException>(() => new NetworkSession(shortDefs, true));
        }

        [Test]
        public void 非主机调用主机接口抛异常()
        {
            var client = new NetworkSession(HostConfig(), false);

            Assert.Throws<InvalidOperationException>(() => client.HandleHello(1, 1));
            Assert.Throws<InvalidOperationException>(() => client.TryStartGame());
            Assert.Throws<InvalidOperationException>(() => client.HandleDisconnect(1));
        }

        [Test]
        public void 主机调用客户端接口抛异常()
        {
            var host = new NetworkSession(HostConfig(), true);

            Assert.Throws<InvalidOperationException>(() => host.ApplyJoinAccept(1, 1, new[] { 1, 2, 3, 4 }));
        }
    }
}
