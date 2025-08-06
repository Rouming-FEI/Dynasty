using Core.GAS;

namespace Gameplay.Combat
{
    public static class ShieldSystem
    {
        public static void GrantShield(AbilitySystemComponent asc, int sourceId)
        {
            if (asc == null || asc.IsDead) return;
            asc.Tags.AddTag(GameplayTagConstants.HasShield);
            asc.SendEvent(GameplayEventTypes.OnShieldGranted, sourceId, asc.OwnerId, 0);
        }

        public static bool HasShield(AbilitySystemComponent asc)
        {
            return asc != null && asc.Tags.HasTag(GameplayTagConstants.HasShield);
        }
    }
}
