using Gameplay.Characters;
using Core;
using Core.GAS;
using Core.Command;

namespace Gameplay.Judgment
{
    public class JudgmentCommand : ICommand
    {
        public int Tick { get; set; }
        public int PlayerId { get; set; }
        public ChipAllocation Allocation;

        public JudgmentCommand() { }
        public JudgmentCommand(int tick, int playerId, ChipAllocation allocation)
        {
            Tick = tick;
            PlayerId = playerId;
            Allocation = allocation;
        }

        public void Execute(WorldState state)
        {
            // JudgmentCommand doesn't resolve in Execute.
            // Resolution is done by TurnManager after collecting all players' chips.
        }
    }

    public class SetAPCommand : ICommand
    {
        public int Tick { get; set; }
        public int PlayerId { get; set; }
        public int Amount;

        public SetAPCommand() { }
        public SetAPCommand(int tick, int playerId, int amount)
        {
            Tick = tick;
            PlayerId = playerId;
            Amount = amount;
        }

        public void Execute(WorldState state)
        {
            Character c = state.GetCharacterByPlayer(PlayerId);
            if (c != null)
            {
                // Set AP to exact amount
                c.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, Amount);
            }
        }
    }
}
