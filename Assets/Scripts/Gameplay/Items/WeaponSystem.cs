using Core;
using Core.GAS;
using Gameplay.Characters;

namespace Gameplay.Items
{
    /// <summary>
    /// 武器系统：统一处理远程攻击、投掷、拆除、装填等武器操作。
    /// 所有方法均为静态，零堆分配（Hot Path 安全）。
    /// </summary>
    public static class WeaponSystem
    {
        /// <summary>
        /// 远程攻击：不限同地点，消耗 1 弹药对目标造成伤害。
        /// 返回 false 表示弹药不足，攻击未执行。
        /// </summary>
        public static bool RangedAttack(WorldState state, Character attacker, Character target, ItemDef weapon, float damage)
        {
            if (attacker.Inventory.EquippedWeaponAmmo <= 0) return false;
            attacker.Inventory.EquippedWeaponAmmo--;
            DamagePipeline.ApplyDamage(state, attacker.Id, target.Id, damage, DamageType.Physical);
            return true;
        }

        /// <summary>
        /// 投掷施工锤：攻击方 vs 目标 RPS 判定，命中造成 1.5 理智伤害。
        /// 无论命中与否，锤子掉落在目标位置并解除装备。
        /// 返回 true 表示命中。
        /// </summary>
        public static bool ThrowHammer(WorldState state, Character attacker, Character target, ItemDef hammer)
        {
            // 简易 RPS：确定性随机 50/50
            bool hit = state.Random.Next(2) == 0;
            if (hit)
                DamagePipeline.ApplyDamage(state, attacker.Id, target.Id, 1.5f, DamageType.Physical);

            // 锤子掉落在目标位置
            state.Locations.DropItem(target.CurrentLocationId, hammer.Id, 1);

            // 解除装备
            attacker.Inventory.EquippedWeapon = null;
            return hit;
        }

        /// <summary>
        /// 装填枪械：将弹药数设为 1。不消耗 AP（AP 由命令层扣除）。
        /// </summary>
        public static void Reload(Character c)
        {
            c.Inventory.EquippedWeaponAmmo = 1;
        }

        /// <summary>
        /// 拆除当前地点：清除该地点所有掉落物。
        /// </summary>
        public static void DemolishLocation(WorldState state, Character c)
        {
            state.Locations.ClearItems(c.CurrentLocationId);
        }
    }
}
