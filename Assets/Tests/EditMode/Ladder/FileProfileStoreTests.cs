using System;
using System.IO;
using Gameplay.Ladder;
using NUnit.Framework;

namespace Dynasty.Tests.Ladder
{
    /// <summary>FileProfileStore：临时目录往返/缺文件/坏文件/版本头/行级容错。SetUp/TearDown 每用例独立临时目录。</summary>
    public class FileProfileStoreTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ladder-fps-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void TearDown()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }
            catch (IOException) { }
        }

        private string FilePath => Path.Combine(_dir, "ladder-profile.dat");

        [Test]
        public void MissingFile_LoadsZeroProfile()
        {
            var store = new FileProfileStore(_dir);
            LadderProfile p = store.Load();
            Assert.AreEqual(0, p.BestStreak);
            Assert.AreEqual(0, p.Attempts);
            Assert.IsFalse(p.HasActiveRun);
        }

        [Test]
        public void SaveThenLoad_RoundTripsAllFields()
        {
            var store = new FileProfileStore(_dir);
            var p = default(LadderProfile).Apply(4, 100);
            p.HasActiveRun = true;
            p.ActiveSeed = 777;
            p.ActiveLayer = 3;
            store.Save(p);

            LadderProfile q = new FileProfileStore(_dir).Load();
            Assert.AreEqual(4, q.BestStreak);
            Assert.AreEqual(100L, q.TotalScore);
            Assert.AreEqual(1, q.Attempts);
            Assert.AreEqual(4, q.LastStreak);
            Assert.AreEqual(100L, q.LastScore);
            Assert.IsTrue(q.HasActiveRun);
            Assert.AreEqual(777, q.ActiveSeed);
            Assert.AreEqual(3, q.ActiveLayer);
        }

        [Test]
        public void CorruptFile_LoadsZeroProfile_WithoutThrowing()
        {
            new FileProfileStore(_dir).Save(default(LadderProfile));   // 先落真实档案再覆写破坏——防坏文件路径走缺文件分支假绿
            File.WriteAllText(FilePath, "这不是档案格式\n随便写的坏内容");
            var store = new FileProfileStore(_dir);
            LadderProfile p = store.Load();       // 不抛
            Assert.AreEqual(0, p.BestStreak);
        }

        [Test]
        public void WrongVersionHeader_LoadsZeroProfile()
        {
            new FileProfileStore(_dir).Save(default(LadderProfile));   // 先落真实档案再覆写破坏（同上）
            File.WriteAllText(FilePath, "dynasty-ladder-profile v999\nbestStreak=7\n");
            var store = new FileProfileStore(_dir);
            Assert.AreEqual(0, store.Load().BestStreak);
        }

        [Test]
        public void Save_OverwritesPrevious()
        {
            var store = new FileProfileStore(_dir);
            store.Save(default(LadderProfile).Apply(2, 30));
            store.Save(default(LadderProfile).Apply(5, 150));
            Assert.AreEqual(5, store.Load().BestStreak);
            Assert.AreEqual(150L, store.Load().TotalScore);
            Assert.AreEqual(1, store.Load().Attempts);   // 覆盖=全量替换：终值 = 最后 Save 的档案（default.Apply 自 0 起 Attempts=1）
        }

        [Test]
        public void HeaderThenMixedBadLines_LineLevelFallback()
        {
            // 合法 v1 头后的坏行/坏值/未知 key/空行：行级降级——不抛、好字段保留、坏字段归零
            File.WriteAllText(FilePath,
                "dynasty-ladder-profile v1\n" +
                "bestStreak=abc\n" +
                "totalScore=150\n" +
                "这是没有等号的行\n" +
                "unknownKey=42\n" +
                "\n" +
                "attempts=3\n");
            var store = new FileProfileStore(_dir);
            LadderProfile p = store.Load();          // 不抛
            Assert.AreEqual(0, p.BestStreak);        // 坏值 → 0
            Assert.AreEqual(150L, p.TotalScore);     // 好字段保留
            Assert.AreEqual(3, p.Attempts);          // 好字段保留
            Assert.IsFalse(p.HasActiveRun);          // 缺失行 → 门控 false
        }
    }
}
