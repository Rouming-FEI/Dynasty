using Gameplay.Characters;
using Core;
using Core.GAS;
using Core.Command;

namespace Gameplay.Movement
{
    public class MoveCommand : ICommand
    {
        public int Tick { get; set; }
        public int PlayerId { get; set; }
        public int TargetLocationId { get; set; }

        public MoveCommand() { }
        public MoveCommand(int tick, int playerId, int targetId)
        {
            Tick = tick; PlayerId = playerId; TargetLocationId = targetId;
        }

        public void Execute(WorldState state)
        {
            Character c = state.GetCharacterByPlayer(PlayerId);
            if (c == null || !c.CanMove) return;
            if (!state.Locations.IsValidLocation(TargetLocationId)) return;
            if (c.ActionPoints <= 0) return;

            // Spend 1 AP
            c.ASC.ModifyAttributeCurrentValue(AttributeConstants.ActionPoints, -1f);

            int oldLoc = c.CurrentLocationId;
            c.CurrentLocationId = TargetLocationId;

            var evt = new GameplayEvent(GameplayEventTypes.EntityMoved, PlayerId, c.Id)
            {
                IntArg1 = oldLoc,
                IntArg2 = TargetLocationId
            };
            state.Events.Dispatch(GameplayEventTypes.EntityMoved, ref evt);
        }
    }

    public class WasteAPCommand : ICommand
    {
        public int Tick { get; set; }
        public int PlayerId { get; set; }

        public WasteAPCommand() { }
        public WasteAPCommand(int tick, int playerId) { Tick = tick; PlayerId = playerId; }

        public void Execute(WorldState state)
        {
            Character c = state.GetCharacterByPlayer(PlayerId);
            if (c == null || c.ActionPoints <= 0) return;
            c.ASC.ModifyAttributeCurrentValue(AttributeConstants.ActionPoints, -1f);
        }
    }
}
