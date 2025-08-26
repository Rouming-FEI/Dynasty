using System;
using Core;
using Core.Command;
using Core.GAS;
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

            // Find enemy at same location (for combat) and any alive enemy (for movement)
            int sameLocEnemy = -1;
            int anyEnemy = -1;
            for (int i = 0; i < state.PlayerCount; i++)
            {
                Character other = state.Characters[i];
                if (other == null || other.IsDead || other.PlayerId == playerId) continue;
                anyEnemy = other.PlayerId; // just grab first alive enemy
                if (other.CurrentLocationId == c.CurrentLocationId)
                    sameLocEnemy = other.PlayerId;
            }

            ICommand cmd;
            if (sameLocEnemy >= 0)
            {
                // Enemy at same location — exclusive ranges:
                //   [ 0, 35)  35%  use skill on enemy
                //   [35, 70)  35%  attack with weapon
                //   [70, 85)  15%  heal if hurt
                //   [85,100)  15%  move away
                if (roll < 35)
                    cmd = TryUseSkill(state, c, playerId, sameLocEnemy);
                else if (roll < 70)
                    cmd = TryAttack(state, c, playerId, sameLocEnemy);
                else if (roll < 85)
                    cmd = TryHeal(state, c, playerId);
                else
                    cmd = TryMove(state, c, playerId);
            }
            else
            {
                // No enemy here — exclusive ranges:
                //   [ 0, 65)  65%  move toward enemies
                //   [65, 80)  15%  heal if hurt
                //   [80,100)  20%  waste AP
                if (roll < 65)
                    cmd = anyEnemy >= 0 ? TryMove(state, c, playerId) : null;
                else if (roll < 80)
                    cmd = TryHeal(state, c, playerId);
                else
                    cmd = null;
            }

            // Any range whose action is unavailable falls back to wasting AP,
            // never into another range's action.
            return cmd ?? new WasteAPCommand(state.CurrentTick, playerId);
        }

        private static ICommand TryUseSkill(WorldState state, Character c, int playerId, int targetId)
        {
            if (c.Definition?.Skills == null || c.Definition.Skills.Length == 0) return null;

            // Single pass over CanActivate: collect valid indices, then pick one (zero GC)
            Span<int> validSkills = stackalloc int[c.Definition.Skills.Length];
            int vc = 0;
            for (int i = 0; i < c.Definition.Skills.Length; i++)
                if (SkillSystem.CanActivate(c.Definition.Skills[i], c))
                    validSkills[vc++] = i;
            if (vc == 0) return null;

            int skillIdx = validSkills[state.Random.Next(vc)];

            // 自 Buff 技能（守护/迷离之境）目标是施放者自己，不能把 Buff 丢给敌人
            SkillDef skill = c.Definition.Skills[skillIdx];
            int target = skill.Target == SkillTargetType.Self ? playerId : targetId;
            return new UseSkillCommand(state.CurrentTick, playerId, skillIdx, target);
        }

        private static ICommand TryAttack(WorldState state, Character c, int playerId, int targetId)
        {
            if (c.Inventory.EquippedWeapon == null) return null;
            return new DamageCommand(state.CurrentTick, playerId, targetId, 1f);
        }

        private static ICommand TryHeal(WorldState state, Character c, int playerId)
        {
            if (c.Sanity >= c.MaxSanity) return null;
            for (int i = 0; i < c.Inventory.Consumables.Count; i++)
            {
                ItemDef item = c.Inventory.Consumables[i];
                if (item != null && item.TargetType == ItemTargetType.Self)
                    return new UseItemCommand(state.CurrentTick, playerId, i, playerId);
            }
            return null;
        }

        private static ICommand TryMove(WorldState state, Character c, int playerId)
        {
            // Simple movement: step to a random connected location (map hub topology
            // makes this converge toward enemies over a few turns)
            int[] connections = state.Locations.GetConnectedLocations(c.CurrentLocationId);
            if (connections == null || connections.Length == 0) return null;
            int target = connections[state.Random.Next(connections.Length)];
            return new MoveCommand(state.CurrentTick, playerId, target);
        }
    }
}
