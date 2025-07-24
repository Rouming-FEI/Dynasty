namespace Dynasty.Core
{
    public interface ICommand
    {
        int Tick { get; }
        int PlayerId { get; }
        void Execute(WorldState state);
    }
}
