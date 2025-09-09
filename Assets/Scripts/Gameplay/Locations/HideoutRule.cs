using Core;
using Core.GAS;
using Gameplay.Characters;

namespace Gameplay.Locations
{
    /// <summary>
    /// 藏匿处(6)规则：进入时获得 HideoutHidden 标签，离开时移除。
    /// 隐身角色无法被外部角色选中（除非对方也处于藏匿状态），并免疫核弹伤害。
    /// </summary>
    public class HideoutRule : LocationRule
    {
        public HideoutRule() { LocationId = 6; }

        public override void OnEnter(WorldState state, Character c)
        {
            c.ASC.Tags.AddTag(GameplayTagConstants.HideoutHidden);
        }

        public override void OnLeave(WorldState state, Character c)
        {
            c.ASC.Tags.RemoveTag(GameplayTagConstants.HideoutHidden);
        }

        public override bool CanTarget(Character viewer, Character target)
        {
            if (!target.ASC.Tags.HasTag(GameplayTagConstants.HideoutHidden)) return true;
            // 隐身目标只能被同样处于藏匿状态的角色选中
            return viewer.ASC.Tags.HasTag(GameplayTagConstants.HideoutHidden);
        }
    }
}
