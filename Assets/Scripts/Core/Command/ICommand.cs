using Core;
namespace Core.Command
{
    public interface ICommand
    {
        int Tick { get; }
        int PlayerId { get; }
        void Execute(WorldState state);
    }
}
