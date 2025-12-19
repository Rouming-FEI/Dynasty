using Core;
using Core.Command;
using Core.GAS;

namespace Gameplay.Characters
{
    /// <summary>被束缚者解除束缚：1AP。</summary>
    public class UnbindCommand : ICommand
    {
        public int Tick { get; set; }
        public int PlayerId { get; set; }
        public UnbindCommand() { }
        public UnbindCommand(int tick, int playerId) { Tick = tick; PlayerId = playerId; }

        public void Execute(WorldState state)
        {
            Character c = state.GetCharacterByPlayer(PlayerId);
            if (c == null || c.IsDead || c.ActionPoints <= 0) return;
            if (!c.ASC.Tags.HasTag(GameplayTagConstants.Bound)) return;
            c.ASC.ModifyAttributeCurrentValue(AttributeConstants.ActionPoints, -1f);
            c.ASC.Tags.RemoveTag(GameplayTagConstants.Bound);
            c.ASC.Tags.RemoveTag(GameplayTagConstants.BoundStrong);
            // 移除束缚 GE（Id=310）
            c.ASC.RemoveEffectsById(310);
        }
    }
}
