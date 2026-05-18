using System;
using System.IO;
using Gameplay.Ladder;
using NUnit.Framework;

namespace Dynasty.Tests.Ladder
{
    /// <summary>FileProfileStore：临时目录往返/缺文件/坏文件/版本头。每用例独立临时目录（TestContext 清理）。</summary>
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
            File.WriteAllText(FilePath, "这不是档案格式\n随便写的坏内容");
            var store = new FileProfileStore(_dir);
            LadderProfile p = store.Load();       // 不抛
            Assert.AreEqual(0, p.BestStreak);
        }

        [Test]
        public void WrongVersionHeader_LoadsZeroProfile()
        {
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
    }
}
