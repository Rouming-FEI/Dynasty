using Gameplay.Ladder;
using NUnit.Framework;

namespace Dynasty.Tests.Ladder
{
    public class InMemoryProfileStoreTests
    {
        [Test]
        public void EmptyStore_LoadsZeroProfile()
        {
            var store = new InMemoryProfileStore();
            LadderProfile p = store.Load();
            Assert.AreEqual(0, p.BestStreak);
            Assert.AreEqual(0, p.Attempts);
        }

        [Test]
        public void SaveThenLoad_RoundTrips()
        {
            var store = new InMemoryProfileStore();
            LadderProfile p = default(LadderProfile).Apply(4, 100);
            store.Save(p);
            LadderProfile q = store.Load();
            Assert.AreEqual(4, q.BestStreak);
            Assert.AreEqual(100L, q.TotalScore);
            Assert.AreEqual(1, q.Attempts);
        }

        [Test]
        public void SeededStore_LoadsInitialProfile()
        {
            var store = new InMemoryProfileStore(default(LadderProfile).Apply(2, 30));
            Assert.AreEqual(2, store.Load().BestStreak);
        }
    }
}
