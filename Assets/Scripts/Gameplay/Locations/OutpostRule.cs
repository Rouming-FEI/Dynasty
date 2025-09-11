using Core;
using Core.GAS;
using Gameplay.Characters;

namespace Gameplay.Locations
{
    /// <summary>
    /// 地狱前哨(8)规则：重质量炮。
    /// 操作链：上塔(1AP) → 装弹(1AP) → 瞄准(1AP) → 发射(1AP，对任意玩家 3 物理伤害，不限距离)。
    /// 发射后清除装弹/瞄准状态，需重新装弹+瞄准才能再次发射；下塔(1AP)自受 1 物理伤害。
    ///
    /// 上塔时同时挂 OnTurret + CannotAct 标签：现有 CanAct/CanMove 检查
    /// 自动阻断移动/攻击/道具/技能等其他行动；炮台操作命令自身不检查 CanAct
    /// （只检查 OnTurret + 存活），见 OutpostCommands。
    /// 炮台状态（装弹/瞄准）以标签形式存储在操作者 ASC 上。
    /// </summary>
    public class OutpostRule : LocationRule
    {
        public const int OutpostLocationId = 8;

        public OutpostRule() { LocationId = OutpostLocationId; }

        /// <summary>
        /// 兜底：角色在塔上却以任何方式离开地点时（正常流程会被 CannotAct 阻断移动，
        /// 此处防御未来的强制位移机制），清除全部炮台状态标签。
        /// </summary>
        public override void OnLeave(WorldState state, Character c)
        {
            if (!c.ASC.Tags.HasTag(GameplayTagConstants.OnTurret)) return;
            ClearTurretTags(c);
        }

        /// <summary>清除角色身上的全部炮台标签（含上塔附带的 CannotAct）。</summary>
        public static void ClearTurretTags(Character c)
        {
            c.ASC.Tags.RemoveTag(GameplayTagConstants.OnTurret);
            c.ASC.Tags.RemoveTag(GameplayTagConstants.TurretLoaded);
            c.ASC.Tags.RemoveTag(GameplayTagConstants.TurretAimed);
            c.ASC.Tags.RemoveTag(GameplayTagConstants.CannotAct);
        }
    }
}
