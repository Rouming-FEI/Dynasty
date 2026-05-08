namespace Gameplay.Ladder
{
    /// <summary>档案存取抽象。Simulation 层无 IO——Unity 文件实现（persistentDataPath）在
    /// 正式网络对战 UI 阶段按本接口补齐，Simulation 零改动。</summary>
    public interface IProfileStore
    {
        LadderProfile Load();
        void Save(LadderProfile profile);
    }
}
