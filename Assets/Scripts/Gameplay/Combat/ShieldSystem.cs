using Core;
using Core.GAS;

namespace Gameplay.Combat
{
    public static class ShieldSystem
    {
        public static void GrantShield(WorldState state, AbilitySystemComponent asc, int sourceId)
        {
            if (asc == null || asc.IsDead) return;
            asc.Tags.AddTag(GameplayTagConstants.HasShield);

            // ASC 内部事件（供 Buff/被动响应）
            asc.SendEvent(GameplayEventTypes.OnShieldGranted, sourceId, asc.OwnerId, 0);

            // 全局事件（供 UI/日志等外部订阅者响应）
            var evt = new GameplayEvent(GameplayEventTypes.OnShieldGranted, sourceId, asc.OwnerId);
            state.Events.Dispatch(GameplayEventTypes.OnShieldGranted, ref evt);
        }

        public static bool HasShield(AbilitySystemComponent asc)
        {
            return asc != null && asc.Tags.HasTag(GameplayTagConstants.HasShield);
        }
    }
}
