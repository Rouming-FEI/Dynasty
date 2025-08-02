using Core;
using Core.Command;
using Gameplay.Character;
using Gameplay.Combat;
using Gameplay.Judgment;
using Gameplay.Movement;

namespace AI
{
    public class SimpleAI
    {
        private int _playerId;

        public SimpleAI(int playerId)
        {
            _playerId = playerId;
        }

        public ChipAllocation GetJudgmentInput(WorldState state, int playerId)
        {
            ChipAllocation allocation = new ChipAllocation(playerId);
            allocation.SetRandom(state.Random);
            return allocation;
        }

        public ICommand GetActionInput(WorldState state, int playerId)
        {
            Character c = state.GetCharacterByPlayer(playerId);
            if (c == null || c.IsDead || c.ActionPoints <= 0) return null;

            int roll = state.Random.Next(100);

            if (roll < 60)
            {
                // 60%: move to random connected location
                int[] connections = state.Locations.GetConnectedLocations(c.CurrentLocationId);
                if (connections != null && connections.Length > 0)
                {
                    int target = connections[state.Random.Next(connections.Length)];
                    return new MoveCommand(state.CurrentTick, playerId, target);
                }
            }
            else if (roll < 80)
            {
                // 20%: attack random alive player at same location
                int targetId = -1;
                for (int i = 0; i < state.PlayerCount; i++)
                {
                    Character other = state.Characters[i];
                    if (other != null && !other.IsDead && other.PlayerId != playerId
                        && other.CurrentLocationId == c.CurrentLocationId)
                    {
                        targetId = other.PlayerId;
                        break;
                    }
                }
                if (targetId >= 0)
                    return new DamageCommand(state.CurrentTick, playerId, targetId, 1f);
            }

            // Fallback: waste AP
            return new WasteAPCommand(state.CurrentTick, playerId);
        }
    }
}
