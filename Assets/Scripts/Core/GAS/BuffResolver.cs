using System;
using System.Collections.Generic;

namespace Core.GAS
{
    /// <summary>
    /// Resolves derived-attribute modifiers from active GameplayEffects each turn.
    /// Only derived combat attributes (PhysicalDefense / MagicDefense / AttackBonus)
    /// are reset to base and rebuilt; persistent resources (Sanity, ActionPoints)
    /// are never touched — those are mutated by Instant / periodic effects only.
    /// Modifiers are flattened, sorted by op priority (Override > Multiply > Add)
    /// then by Channel ascending, and applied on top of the fresh base values.
    /// Uses stackalloc for the temporary modifier buffer — zero GC on the Hot Path.
    /// </summary>
    public static class BuffResolver
    {
        // 对活跃 GE 中作用于派生属性的 Modifier 做拓扑排序后重建
        // 规则：Override > Multiply > Add；同优先级按 Channel 升序
        // activeEffects 为空时也必须调用：负责把已过期 GE 残留的派生属性清回基础值
        public static void Resolve(
            List<GameplayEffectSpec> activeEffects,
            AttributeSet attributeSet)
        {
            // 1. 仅重置派生属性到基础值（Sanity / ActionPoints 等持久资源不动）
            attributeSet.ResetAttribute(AttributeConstants.PhysicalDefense);
            attributeSet.ResetAttribute(AttributeConstants.MagicDefense);
            attributeSet.ResetAttribute(AttributeConstants.AttackBonus);

            if (activeEffects.Count == 0) return;

            // 2. 统计作用于派生属性的 modifier 数量
            int modCount = 0;
            for (int i = 0; i < activeEffects.Count; i++)
            {
                AttributeModifier[] mods = activeEffects[i]?.Definition?.Modifiers;
                if (mods == null) continue;
                for (int j = 0; j < mods.Length; j++)
                {
                    if (IsDerivedAttribute(mods[j].AttributeId))
                        modCount++;
                }
            }

            if (modCount == 0) return;

            // 3. 展开到 span（栈分配），跳过非派生属性的 modifier
            Span<AttributeModifier> allMods = stackalloc AttributeModifier[modCount];
            int idx = 0;
            for (int i = 0; i < activeEffects.Count; i++)
            {
                AttributeModifier[] mods = activeEffects[i]?.Definition?.Modifiers;
                if (mods == null) continue;
                for (int j = 0; j < mods.Length; j++)
                {
                    if (IsDerivedAttribute(mods[j].AttributeId))
                        allMods[idx++] = mods[j];
                }
            }

            // 4. 稳定排序：Op 优先 + Channel
            for (int i = 0; i < modCount - 1; i++)
            {
                for (int j = 0; j < modCount - 1 - i; j++)
                {
                    if (CompareModifierPriority(allMods[j], allMods[j + 1]) > 0)
                        (allMods[j], allMods[j + 1]) = (allMods[j + 1], allMods[j]);
                }
            }

            // 5. Apply
            for (int i = 0; i < modCount; i++)
            {
                ApplyModifier(attributeSet, allMods[i]);
            }
        }

        /// <summary>
        /// Derived attributes are fully recomputed from base + active buffs each
        /// resolve. Everything else (Sanity, ActionPoints, caps) is persistent.
        /// </summary>
        private static bool IsDerivedAttribute(int attributeId) =>
            attributeId == AttributeConstants.PhysicalDefense ||
            attributeId == AttributeConstants.MagicDefense ||
            attributeId == AttributeConstants.AttackBonus;

        private static int CompareModifierPriority(AttributeModifier a, AttributeModifier b)
        {
            int pa = OpPriority(a.Op), pb = OpPriority(b.Op);
            if (pa != pb) return pa.CompareTo(pb);
            return a.Channel.CompareTo(b.Channel);
        }

        private static int OpPriority(AttributeModifierOp op) => op switch
        {
            AttributeModifierOp.Override => 0,
            AttributeModifierOp.Multiply => 1,
            AttributeModifierOp.Add => 2,
            _ => 3
        };

        private static void ApplyModifier(AttributeSet set, AttributeModifier mod)
        {
            float cur = set.GetValue(mod.AttributeId);
            switch (mod.Op)
            {
                case AttributeModifierOp.Override:
                    set.SetCurrentValue(mod.AttributeId, mod.Magnitude);
                    break;
                case AttributeModifierOp.Add:
                    set.SetCurrentValue(mod.AttributeId, cur + mod.Magnitude);
                    break;
                case AttributeModifierOp.Multiply:
                    set.SetCurrentValue(mod.AttributeId, cur * (1f + mod.Magnitude));
                    break;
            }
        }
    }
}
