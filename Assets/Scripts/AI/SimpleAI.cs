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

            if (roll < 40)
            {
                // 40%: move to random connected location
                int[] connections = state.Locations.GetConnectedLocations(c.CurrentLocationId);
                if (connections != null && connections.Length > 0)
                {
                    int target = connections[state.Random.Next(connections.Length)];
                    return new MoveCommand(state.CurrentTick, playerId, target);
                }
            }
            else if (roll < 60)
            {
                // 20%: use random valid skill on random enemy at same location
                if (c.Definition?.Skills != null && c.Definition.Skills.Length > 0)
                {
                    // Count enemies at same location
                    int enemyCount = 0;
                    for (int i = 0; i < state.PlayerCount; i++)
                    {
                        Character other = state.Characters[i];
                        if (other != null && !other.IsDead && other.PlayerId != playerId
                            && other.CurrentLocationId == c.CurrentLocationId)
                            enemyCount++;
                    }

                    if (enemyCount > 0)
                    {
                        // Count valid skills (check AP + tag requirements via SkillSystem)
                        int validCount = 0;
                        for (int i = 0; i < c.Definition.Skills.Length; i++)
                        {
                            if (SkillSystem.CanActivate(c.Definition.Skills[i], c))
                                validCount++;
                        }

                        if (validCount > 0)
                        {
                            // Pick random valid skill (two-pass: no allocation)
                            int picked = state.Random.Next(validCount);
                            int skillIdx = -1;
                            int pos = 0;
                            for (int i = 0; i < c.Definition.Skills.Length; i++)
                            {
                                if (SkillSystem.CanActivate(c.Definition.Skills[i], c))
                                {
                                    if (pos == picked) { skillIdx = i; break; }
                                    pos++;
                                }
                            }

                            // Pick random enemy at same location (two-pass: no allocation)
                            int enemyPicked = state.Random.Next(enemyCount);
                            int targetId = -1;
                            int enemyPos = 0;
                            for (int i = 0; i < state.PlayerCount; i++)
                            {
                                Character other = state.Characters[i];
                                if (other != null && !other.IsDead && other.PlayerId != playerId
                                    && other.CurrentLocationId == c.CurrentLocationId)
                                {
                                    if (enemyPos == enemyPicked) { targetId = other.PlayerId; break; }
                                    enemyPos++;
                                }
                            }

                            if (skillIdx >= 0 && targetId >= 0)
                                return new UseSkillCommand(state.CurrentTick, playerId, skillIdx, targetId);
                        }
                    }
                }
            }
            else if (roll < 80)
            {
                // 20%: use health pack (Self-target consumable) if sanity < 2
                if (c.Sanity < 2f)
                {
                    for (int i = 0; i < c.Inventory.Consumables.Count; i++)
                    {
                        ItemDef item = c.Inventory.Consumables[i];
                        if (item != null && item.TargetType == ItemTargetType.Self)
                        {
                            return new UseItemCommand(state.CurrentTick, playerId, i, playerId);
                        }
                    }
                }
            }
            else if (roll < 90)
            {
                // 10%: attack random enemy at same location with knife (damage command)
                for (int i = 0; i < state.PlayerCount; i++)
                {
                    Character other = state.Characters[i];
                    if (other != null && !other.IsDead && other.PlayerId != playerId
                        && other.CurrentLocationId == c.CurrentLocationId)
                    {
                        return new DamageCommand(state.CurrentTick, playerId, other.PlayerId, 1f);
                    }
                }
            }

            // Fallback: waste AP
            return new WasteAPCommand(state.CurrentTick, playerId);
        }
    }
}
