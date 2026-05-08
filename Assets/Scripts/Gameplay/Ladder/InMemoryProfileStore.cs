namespace Gameplay.Ladder
{
    /// <summary>内存档案存储（测试/默认实现；无档案时 Load 返回零值快照）。</summary>
    public sealed class InMemoryProfileStore : IProfileStore
    {
        private LadderProfile _profile;

        public InMemoryProfileStore() { }

        public InMemoryProfileStore(LadderProfile initial)
        {
            _profile = initial;
        }

        public LadderProfile Load() => _profile;

        public void Save(LadderProfile profile) => _profile = profile;
    }
}
