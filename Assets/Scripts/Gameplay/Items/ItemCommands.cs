using Core;
using Core.Command;
using Core.GAS;
using Gameplay.Characters;

namespace Gameplay.Items
{
    /// <summary>拾取道具：消耗 1 AP，将道具放入背包。背包满则拾取失败且不消耗 AP。</summary>
    public class PickupItemCommand : ICommand
    {
        public int Tick { get; set; }
        public int PlayerId { get; set; }
        public int ItemId { get; set; }

        public PickupItemCommand() { }
        public PickupItemCommand(int tick, int playerId, int itemId)
        {
            Tick = tick; PlayerId = playerId; ItemId = itemId;
        }

        public void Execute(WorldState state)
        {
            Character c = state.GetCharacterByPlayer(PlayerId);
            if (c == null || !c.CanAct || c.ActionPoints <= 0) return;

            ItemDef item = ItemRegistry.Get(ItemId);
            if (item == null) return;

            // 背包满 → 拾取失败，不扣 AP
            if (!c.Inventory.AddConsumable(item)) return;

            c.ASC.ModifyAttributeCurrentValue(AttributeConstants.ActionPoints, -1f);
        }
    }

    /// <summary>
    /// 使用道具（刀/血包）：目标合法性校验 → 扣 AP → 对目标应用 UseEffect → 移出背包。
    /// Self 类道具只能对自己使用；SameLocation 类道具要求双方同地点。
    /// </summary>
    public class UseItemCommand : ICommand
    {
        public int Tick { get; set; }
        public int PlayerId { get; set; }
        public int ItemIndex { get; set; }
        public int TargetPlayerId { get; set; }

        public UseItemCommand() { }
        public UseItemCommand(int tick, int playerId, int itemIndex, int targetPlayerId)
        {
            Tick = tick; PlayerId = playerId; ItemIndex = itemIndex; TargetPlayerId = targetPlayerId;
        }

        public void Execute(WorldState state)
        {
            Character c = state.GetCharacterByPlayer(PlayerId);
            if (c == null || !c.CanAct) return;

            if (ItemIndex < 0 || ItemIndex >= c.Inventory.Consumables.Count) return;
            ItemDef item = c.Inventory.Consumables[ItemIndex];
            if (item == null) return;
            if (c.ActionPoints < item.UseAPCost) return;

            Character target = state.GetCharacterByPlayer(TargetPlayerId);
            if (target == null || target.IsDead) return;

            // 目标合法性：Self 只能对自己用（血包），SameLocation 要求同地点（刀）
            switch (item.TargetType)
            {
                case ItemTargetType.Self:
                    if (TargetPlayerId != PlayerId) return;
                    break;
                case ItemTargetType.SameLocation:
                    if (target.CurrentLocationId != c.CurrentLocationId) return;
                    break;
            }

            c.ASC.ModifyAttributeCurrentValue(AttributeConstants.ActionPoints, -(float)item.UseAPCost);

            if (item.UseEffect != null)
            {
                // 伤害类效果（含负 Sanity 修改器，如刀）统一走 DamagePipeline，
                // 保证免疫/护盾/防御/死亡结算顺序一致；其余效果（如血包回复）直接应用
                float damage = GetSanityDamage(item.UseEffect);
                if (damage > 0f)
                {
                    DamagePipeline.ApplyDamage(state, c.Id, target.Id, damage, DamageType.Physical);
                }
                else
                {
                    GameplayEffectSpec spec = target.ASC.RentSpec();
                    spec.Definition = item.UseEffect;
                    spec.SourceId = c.Id;
                    spec.TargetId = target.Id;
                    target.ASC.ApplyGameplayEffect(spec);
                }
            }

            c.Inventory.RemoveConsumable(ItemIndex);
        }

        /// <summary>返回效果中负 Sanity 修改器对应的伤害值（正数）；无伤害修改器返回 0。</summary>
        private static float GetSanityDamage(GameplayEffect effect)
        {
            AttributeModifier[] mods = effect.Modifiers;
            if (mods == null) return 0f;
            for (int i = 0; i < mods.Length; i++)
                if (mods[i].AttributeId == AttributeConstants.Sanity && mods[i].Magnitude < 0f)
                    return -mods[i].Magnitude;
            return 0f;
        }
    }
}
