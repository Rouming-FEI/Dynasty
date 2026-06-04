using System.IO;
using Gameplay.Characters;
using LiteNetLib.Utils;
using Network.Protocol;
using NUnit.Framework;

namespace Dynasty.Tests.Network
{
    /// <summary>GameStart 载荷扩展（最终角色表广播）与 CharacterDefCatalog.AllIds 全集。</summary>
    public class GameStartDefsTests
    {
        [Test]
        public void AllIds_覆盖16角色全集()
        {
            Assert.That(CharacterDefCatalog.AllIds, Is.Not.Null);
            Assert.That(CharacterDefCatalog.AllIds.Length, Is.EqualTo(16), "16 角色全集");
            for (int i = 1; i <= 16; i++)
            {
                Assert.That(CharacterDefCatalog.AllIds, Does.Contain(i), $"缺角色 {i}");
                Assert.That(CharacterDefCatalog.Get(i), Is.Not.Null, $"角色 {i} 无法解析 Def");
            }
        }

        [Test]
        public void GameStart_载荷往返_含全表()
        {
            var w = new NetDataWriter();
            NetProtocol.WriteHeader(w, NetMsgType.GameStart);
            NetProtocol.WriteGameStart(w, 3, new[] { 1, 14, -1, 16 });

            var r = new NetDataReader(w.Data);
            Assert.That(NetProtocol.ReadType(r), Is.EqualTo(NetMsgType.GameStart));
            NetProtocol.ReadGameStart(r, out int turn, out int[] defIds);
            Assert.That(turn, Is.EqualTo(3));
            Assert.That(defIds, Is.EqualTo(new[] { 1, 14, -1, 16 }));
        }

        [Test]
        public void GameStart_超长数组_拒绝()
        {
            // 载荷 = [turn:int][count:int][defIds:int×count]——伪造值必须直接落在 count 字段
            // （WriteGameStart 空表会先写 count=0，尾部追加只能进 defIds 数据区，触发不了防护；
            // 写法对齐 ProtocolTests.JoinAccept_超长defIds_拒绝 负例）
            var w = new NetDataWriter();
            NetProtocol.WriteHeader(w, NetMsgType.GameStart);
            w.Put(1);                   // turn 合法
            w.Put(int.MaxValue / 2);    // 伪造超长表长度 → CheckLength 拒绝
            var r = new NetDataReader(w.Data);
            NetProtocol.ReadType(r);
            Assert.Throws<InvalidDataException>(() => NetProtocol.ReadGameStart(r, out _, out _));
        }

        [Test]
        public void JoinReject_原因常量_稳定()
        {
            Assert.That(NetProtocol.JoinRejectReasonFull, Is.EqualTo(0));
            Assert.That(NetProtocol.JoinRejectReasonVersion, Is.EqualTo(1));
            Assert.That(NetProtocol.JoinRejectReasonRoleTaken, Is.EqualTo(2));
        }
    }
}
