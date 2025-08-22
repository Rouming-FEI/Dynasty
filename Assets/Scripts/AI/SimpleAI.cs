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

            // Find nearest alive enemy (any location)
            int nearestEnemy = -1;
            for (int i = 0; i < state.PlayerCount; i++)
            {
                Character other = state.Characters[i];
                if (other != null && !other.IsDead && other.PlayerId != playerId)
                {
                    nearestEnemy = other.PlayerId; break;
                }
            }

            if (roll < 30 && nearestEnemy >= 0)
            {
                // 30%: use random valid skill on any alive enemy
                if (c.Definition?.Skills != null && c.Definition.Skills.Length > 0)
                {
                    int validCount = 0;
                    for (int i = 0; i < c.Definition.Skills.Length; i++)
                        if (SkillSystem.CanActivate(c.Definition.Skills[i], c)) validCount++;

                    if (validCount > 0)
                    {
                        int skillIdx = 0;
                        int picked = state.Random.Next(validCount);
                        int pos = 0;
                        for (int i = 0; i < c.Definition.Skills.Length; i++)
                        {
                            if (SkillSystem.CanActivate(c.Definition.Skills[i], c))
                            {
                                if (pos == picked) { skillIdx = i; break; }
                                pos++;
                            }
                        }
                        return new UseSkillCommand(state.CurrentTick, playerId, skillIdx, nearestEnemy);
                    }
                }
            }
            else if (roll < 60 && nearestEnemy >= 0 && c.Inventory.EquippedWeapon != null)
            {
                // 30%: attack any alive enemy with equipped weapon
                return new DamageCommand(state.CurrentTick, playerId, nearestEnemy, 1f);
            }
            else if (roll < 75)
            {
                // 15%: use health pack if sanity < max
                if (c.Sanity < c.MaxSanity)
                {
                    for (int i = 0; i < c.Inventory.Consumables.Count; i++)
                    {
                        ItemDef item = c.Inventory.Consumables[i];
                        if (item != null && item.TargetType == ItemTargetType.Self)
                            return new UseItemCommand(state.CurrentTick, playerId, i, playerId);
                    }
                }
            }

            // Fallback: waste AP
            return new WasteAPCommand(state.CurrentTick, playerId);
        }
    }
}
