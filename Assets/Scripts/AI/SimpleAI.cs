using Core;
using Core.Command;
using Gameplay.Characters;
using Gameplay.Combat;
using Gameplay.Items;
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

            // Find enemy at same location (for combat) and nearest enemy (for movement)
            int sameLocEnemy = -1;
            int nearestEnemy = -1;
            int nearestLoc = -1;
            for (int i = 0; i < state.PlayerCount; i++)
            {
                Character other = state.Characters[i];
                if (other == null || other.IsDead || other.PlayerId == playerId) continue;
                nearestEnemy = other.PlayerId; // just grab first alive enemy
                nearestLoc = other.CurrentLocationId;
                if (other.CurrentLocationId == c.CurrentLocationId)
                    sameLocEnemy = other.PlayerId;
            }

            bool hasEnemyHere = sameLocEnemy >= 0;

            if (hasEnemyHere)
            {
                // Enemy at same location → fight!
                if (roll < 35)
                {
                    // 35%: use skill on same-location enemy
                    if (c.Definition?.Skills != null && c.Definition.Skills.Length > 0)
                    {
                        int validCount = 0;
                        for (int i = 0; i < c.Definition.Skills.Length; i++)
                            if (SkillSystem.CanActivate(c.Definition.Skills[i], c)) validCount++;
                        if (validCount > 0)
                        {
                            int skillIdx = 0, picked = state.Random.Next(validCount), pos = 0;
                            for (int i = 0; i < c.Definition.Skills.Length; i++)
                                if (SkillSystem.CanActivate(c.Definition.Skills[i], c))
                                    { if (pos == picked) { skillIdx = i; break; } pos++; }
                            return new UseSkillCommand(state.CurrentTick, playerId, skillIdx, sameLocEnemy);
                        }
                    }
                }
                else if (roll < 70 && c.Inventory.EquippedWeapon != null)
                {
                    // 35%: attack same-location enemy
                    return new DamageCommand(state.CurrentTick, playerId, sameLocEnemy, 1f);
                }
            }

            // Not fighting / fallback: move toward nearest enemy or heal
            if (roll < 60 && nearestEnemy >= 0 && c.CurrentLocationId != nearestLoc)
            {
                // 60%: move toward nearest enemy (simple: move to safehouse as hub)
                int[] connections = state.Locations.GetConnectedLocations(c.CurrentLocationId);
                if (connections != null && connections.Length > 0)
                {
                    int target = connections[state.Random.Next(connections.Length)];
                    return new MoveCommand(state.CurrentTick, playerId, target);
                }
            }
            else if (roll < 75 && c.Sanity < c.MaxSanity)
            {
                // 15%: heal if hurt
                for (int i = 0; i < c.Inventory.Consumables.Count; i++)
                {
                    ItemDef item = c.Inventory.Consumables[i];
                    if (item != null && item.TargetType == ItemTargetType.Self)
                        return new UseItemCommand(state.CurrentTick, playerId, i, playerId);
                }
            }

            // Fallback: waste AP
            return new WasteAPCommand(state.CurrentTick, playerId);
        }
    }
}
