using Core.GAS;
using Gameplay.Characters;

namespace Core
{
    /// <summary>静止时空：AP 翻倍且至少 1。写入角色行动点并返回新值。</summary>
    public static class TimeStopHook
    {
        public static int ApplyApBonus(Character c, int ap)
        {
            if (!c.ASC.Tags.HasTag(GameplayTagConstants.TimeStop)) return ap;
            int doubled = ap * 2;
            if (doubled < 1) doubled = 1;
            c.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, doubled);
            return doubled;
        }
    }
}
