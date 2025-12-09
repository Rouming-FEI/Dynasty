using Core;
using Core.Command;
using Core.GAS;

namespace Gameplay.Characters
{
    /// <summary>拔刀：1AP，归鞘 → 拔刀（减伤消失，蜃景可用）。</summary>
    public class DrawBladeCommand : ICommand
    {
        public int Tick { get; set; }
        public int PlayerId { get; set; }
        public DrawBladeCommand() { }
        public DrawBladeCommand(int tick, int playerId) { Tick = tick; PlayerId = playerId; }

        public void Execute(WorldState state)
        {
            Character c = state.GetCharacterByPlayer(PlayerId);
            if (c == null || c.IsDead || c.ActionPoints <= 0) return;
            if (!c.ASC.Tags.HasTag(GameplayTagConstants.BladeSheathed)) return;
            c.ASC.ModifyAttributeCurrentValue(AttributeConstants.ActionPoints, -1f);
            c.ASC.Tags.RemoveTag(GameplayTagConstants.BladeSheathed);
            c.ASC.Tags.AddTag(GameplayTagConstants.BladeDrawn);
        }
    }

    /// <summary>归鞘：0AP，拔刀 → 归鞘。</summary>
    public class SheatheBladeCommand : ICommand
    {
        public int Tick { get; set; }
        public int PlayerId { get; set; }
        public SheatheBladeCommand() { }
        public SheatheBladeCommand(int tick, int playerId) { Tick = tick; PlayerId = playerId; }

        public void Execute(WorldState state)
        {
            Character c = state.GetCharacterByPlayer(PlayerId);
            if (c == null || c.IsDead) return;
            if (!c.ASC.Tags.HasTag(GameplayTagConstants.BladeDrawn)) return;
            c.ASC.Tags.RemoveTag(GameplayTagConstants.BladeDrawn);
            c.ASC.Tags.AddTag(GameplayTagConstants.BladeSheathed);
        }
    }
}
