using Core;
using Core.GAS;
using Gameplay.Characters;
using Gameplay.Combat;

namespace Gameplay.Items
{
    /// <summary>
    /// 触发式炸弹安置、引爆与远程爆破系统。
    /// 炸弹存储在 WorldState.LocationTrapCounts 中（定长数组，确定性安全）。
    /// </summary>
    public static class TrapSystem
    {
        /// <summary>安置炸弹到地点。由 UseItemCommand (Id=7) 调用。</summary>
        public static void Plant(WorldState state, int locationId, int itemId)
        {
            state.PlantTrap(locationId);
        }

        /// <summary>
        /// 检查地点是否有炸弹，有则引爆：对地点所有角色造成 1.5 效果伤害后移除炸弹。
        /// 在角色进入或行动时调用。返回 true 表示爆炸发生。
        /// </summary>
        public static bool CheckTrigger(WorldState state, int locationId, Character triggerChar)
        {
            if (!state.HasTrap(locationId)) return false;

            state.ClearTrap(locationId);

            var evt = new GameplayEvent(GameplayEventTypes.TrapTriggered, -1, triggerChar?.Id ?? -1)
            {
                IntArg1 = locationId
            };
            state.Events.Dispatch(GameplayEventTypes.TrapTriggered, ref evt);

            // 对地点所有存活角色造成 1.5 效果伤害
            for (int i = 0; i < state.Characters.Length; i++)
            {
                Character c = state.Characters[i];
                if (c == null || c.IsDead) continue;
                if (c.CurrentLocationId != locationId) continue;
                DamagePipeline.ApplyDamage(state, -1, c.Id, 1.5f, DamageType.Effect);
            }

            return true;
        }

        /// <summary>远程引爆指定地点的炸弹，效果同自然触发。</summary>
        public static void RemoteDetonate(WorldState state, int locationId)
        {
            if (!state.HasTrap(locationId)) return;

            state.ClearTrap(locationId);

            var evt = new GameplayEvent(GameplayEventTypes.TrapTriggered, -1, -1)
            {
                IntArg1 = locationId
            };
            state.Events.Dispatch(GameplayEventTypes.TrapTriggered, ref evt);

            for (int i = 0; i < state.Characters.Length; i++)
            {
                Character c = state.Characters[i];
                if (c == null || c.IsDead) continue;
                if (c.CurrentLocationId != locationId) continue;
                DamagePipeline.ApplyDamage(state, -1, c.Id, 1.5f, DamageType.Effect);
            }
        }
    }
}
