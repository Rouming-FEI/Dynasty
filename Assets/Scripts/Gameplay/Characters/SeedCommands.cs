using Core;
using Core.Command;
using Core.GAS;

namespace Gameplay.Characters
{
    /// <summary>木灵种子安置：1AP 于当前位置（续存技能本体）。拆除：1AP，拆除者侵蚀 -0.5。</summary>
    public class SeedPlantCommand : ICommand
    {
        public int Tick { get; set; }
        public int PlayerId { get; set; }
        public SeedPlantCommand() { }
        public SeedPlantCommand(int tick, int playerId) { Tick = tick; PlayerId = playerId; }

        public void Execute(WorldState state)
        {
            Character c = state.GetCharacterByPlayer(PlayerId);
            if (c == null || c.IsDead || c.ActionPoints <= 0) return;
            c.ASC.ModifyAttributeCurrentValue(AttributeConstants.ActionPoints, -1f);
            c.ASC.ModifyAttributeCurrentValue(AttributeConstants.ErosionLimit, -1f);
            state.WoodSeedPlayer = PlayerId;
            state.WoodSeedLocation = c.CurrentLocationId;
        }
    }

    public class SeedRemoveCommand : ICommand
    {
        public int Tick { get; set; }
        public int PlayerId { get; set; }
        public SeedRemoveCommand() { }
        public SeedRemoveCommand(int tick, int playerId) { Tick = tick; PlayerId = playerId; }

        public void Execute(WorldState state)
        {
            Character c = state.GetCharacterByPlayer(PlayerId);
            if (c == null || c.IsDead || c.ActionPoints <= 0) return;
            if (state.WoodSeedPlayer < 0) return;
            c.ASC.ModifyAttributeCurrentValue(AttributeConstants.ActionPoints, -1f);
            c.ASC.ModifyAttributeCurrentValue(AttributeConstants.ErosionLimit, -0.5f);
            state.WoodSeedPlayer = -1;
            state.WoodSeedLocation = -1;
        }
    }
}
