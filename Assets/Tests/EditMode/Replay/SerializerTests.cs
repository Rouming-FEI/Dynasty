using System.IO;
using Core.Replay;
using Gameplay.Judgment;
using NUnit.Framework;

namespace Dynasty.Tests.Replay
{
    public class SerializerTests
    {
        private static ReplayLog BuildSampleLog()
        {
            var log = new ReplayLog
            {
                PlayerCount = 4,
                Seed = 123,
                CharacterDefIds = new[] { 0, 0, 0, 0 },
                MaxTurns = 200,
                TotalTicks = 5,
                HumanPlayersMask = new[] { true, false, false, false },
                TickHashes = new long[] { 11, 22, 33, 44, 55 }
            };
            var round = new ReplayChipRound
            {
                Allocations = new[]
                {
                    new ChipAllocation(0) { Rock = 2, Scissors = 1, Paper = 0 },
                    new ChipAllocation(1) { Rock = 1, Scissors = 2, Paper = 0 },
                    new ChipAllocation(2) { Rock = 0, Scissors = 1, Paper = 2 },
                    new ChipAllocation(3) { Rock = 1, Scissors = 1, Paper = 1 }
                }
            };
            log.Chips.Add(round);
            log.Actions.Add(new ReplayActionRecord
            {
                Tick = 2, PlayerId = 0, Kind = ReplayActionKind.UseSkill,
                IntArg1 = 1, IntArg2 = 2, IntArg3 = 7, FloatArg = 1.25f, Source = 1
            });
            log.Events.Add(new ReplayEventRecord
            {
                Tick = 1, Turn = 1, EventType = 66, SourceId = 1,
                TargetId = 2, Magnitude = 12.5f, Arg1 = 0.5f, Arg2 = -3f,
                IntArg1 = 2, IntArg2 = 9
            });
            return log;
        }

        [Test]
        public void Serialize_RoundTripsEveryField()
        {
            var log = BuildSampleLog();
            string path = Path.Combine(Path.GetTempPath(), $"replay-{TestContext.CurrentContext.Random.Next()}.dynreplay");
            ReplaySerializer.Save(log, path);
            var loaded = ReplaySerializer.Load(path);
            File.Delete(path);

            Assert.That(loaded.Version, Is.EqualTo(log.Version));
            Assert.That(loaded.PlayerCount, Is.EqualTo(4));
            Assert.That(loaded.Seed, Is.EqualTo(123));
            Assert.That(loaded.MaxTurns, Is.EqualTo(200));
            Assert.That(loaded.TotalTicks, Is.EqualTo(5));
            CollectionAssert.AreEqual(log.CharacterDefIds, loaded.CharacterDefIds);
            CollectionAssert.AreEqual(log.HumanPlayersMask, loaded.HumanPlayersMask);
            CollectionAssert.AreEqual(log.TickHashes, loaded.TickHashes);

            Assert.That(loaded.Chips.Count, Is.EqualTo(1));
            Assert.That(loaded.Chips[0].Allocations.Length, Is.EqualTo(4));
            Assert.That(loaded.Chips[0].Allocations[0].Rock, Is.EqualTo(2));
            Assert.That(loaded.Chips[0].Allocations[3].Paper, Is.EqualTo(1));

            Assert.That(loaded.Actions.Count, Is.EqualTo(1));
            Assert.That(loaded.Actions[0].Kind, Is.EqualTo(ReplayActionKind.UseSkill));
            Assert.That(loaded.Actions[0].IntArg1, Is.EqualTo(1));
            Assert.That(loaded.Actions[0].IntArg2, Is.EqualTo(2));
            Assert.That(loaded.Actions[0].IntArg3, Is.EqualTo(7));
            Assert.That(loaded.Actions[0].FloatArg, Is.EqualTo(1.25f));
            Assert.That(loaded.Actions[0].Source, Is.EqualTo(1));

            Assert.That(loaded.Events.Count, Is.EqualTo(1));
            Assert.That(loaded.Events[0].EventType, Is.EqualTo(66));
            Assert.That(loaded.Events[0].Magnitude, Is.EqualTo(12.5f));
            Assert.That(loaded.Events[0].Arg1, Is.EqualTo(0.5f));
            Assert.That(loaded.Events[0].Arg2, Is.EqualTo(-3f));
            Assert.That(loaded.Events[0].IntArg1, Is.EqualTo(2));
            Assert.That(loaded.Events[0].IntArg2, Is.EqualTo(9));
        }

        [Test]
        public void Load_EmptyFile_Throws()
        {
            string path = Path.Combine(Path.GetTempPath(), $"empty-{TestContext.CurrentContext.Random.Next()}.dynreplay");
            File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
            // NUnit 的 Assert.Throws<T> 是精确类型匹配；此处意图是"抛出任意异常"，
            // 派生异常（EndOfStreamException/InvalidDataException）须用 Assert.Catch 接住
            Assert.Catch<System.Exception>(() => ReplaySerializer.Load(path));
            File.Delete(path);
        }

        [Test]
        public void Load_BadMagic_Throws()
        {
            string path = Path.Combine(Path.GetTempPath(), $"badmagic-{TestContext.CurrentContext.Random.Next()}.dynreplay");
            File.WriteAllBytes(path, new byte[] { 0x11, 0x22, 0x33, 0x44 });
            Assert.Throws<InvalidDataException>(() => ReplaySerializer.Load(path));
            File.Delete(path);
        }

        [Test]
        public void Load_BadVersion_Throws()
        {
            string path = Path.Combine(Path.GetTempPath(), $"badversion-{TestContext.CurrentContext.Random.Next()}.dynreplay");
            using (var fs = new FileStream(path, FileMode.Create))
            using (var w = new BinaryWriter(fs))
            {
                w.Write(0x44525950); // Magic 'DRYP'（与 ReplaySerializer.Magic 一致）
                w.Write(99);
            }
            Assert.Throws<InvalidDataException>(() => ReplaySerializer.Load(path));
            File.Delete(path);
        }

        [Test]
        public void Load_TruncatedFile_Throws()
        {
            string path = Path.Combine(Path.GetTempPath(), $"truncated-{TestContext.CurrentContext.Random.Next()}.dynreplay");
            ReplaySerializer.Save(BuildSampleLog(), path);
            byte[] full = File.ReadAllBytes(path);
            File.WriteAllBytes(path, SubArray(full, full.Length / 2));
            // 截断后读取越界抛 EndOfStreamException（System.Exception 的派生类）
            Assert.Catch<System.Exception>(() => ReplaySerializer.Load(path));
            File.Delete(path);
        }

        [Test]
        public void Serialize_NullArrays_RoundTrip()
        {
            var log = BuildSampleLog();
            log.CharacterDefIds = null;
            log.HumanPlayersMask = null;
            log.TickHashes = null;
            string path = Path.Combine(Path.GetTempPath(), $"nulls-{TestContext.CurrentContext.Random.Next()}.dynreplay");
            ReplaySerializer.Save(log, path);
            var loaded = ReplaySerializer.Load(path);
            File.Delete(path);

            Assert.That(loaded.CharacterDefIds, Is.Null);
            Assert.That(loaded.HumanPlayersMask, Is.Null);
            Assert.That(loaded.TickHashes, Is.Null);
        }

        private static byte[] SubArray(byte[] source, int length)
        {
            var result = new byte[length];
            for (int i = 0; i < length; i++) result[i] = source[i];
            return result;
        }
    }
}
