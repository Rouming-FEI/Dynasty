using System;
using System.Collections.Generic;

namespace Core.GAS
{
    /// <summary>
    /// Resolves all attribute modifiers from active GameplayEffects each turn.
    /// Modifiers are flattened, sorted by op priority (Override > Multiply > Add)
    /// then by Channel ascending, and applied on top of a fresh base-value reset.
    /// Uses stackalloc for the temporary modifier buffer — zero GC on the Hot Path.
    /// </summary>
    public static class BuffResolver
    {
        // 对活跃 GE 的 Modifier 做拓扑排序
        // 规则：Override > Multiply > Add；同优先级按 Channel 升序
        public static void Resolve(
            List<GameplayEffectSpec> activeEffects,
            AttributeSet attributeSet)
        {
            if (activeEffects.Count == 0) return;

            // 1. 收集所有 modifier
            int modCount = 0;
            for (int i = 0; i < activeEffects.Count; i++)
            {
                GameplayEffectSpec spec = activeEffects[i];
                if (spec?.Definition?.Modifiers != null)
                    modCount += spec.Definition.Modifiers.Length;
            }

            if (modCount == 0) return;

            // 2. 展开到 span（栈分配）
            Span<AttributeModifier> allMods = stackalloc AttributeModifier[modCount];
            int idx = 0;
            for (int i = 0; i < activeEffects.Count; i++)
            {
                AttributeModifier[] mods = activeEffects[i].Definition.Modifiers;
                if (mods == null) continue;
                for (int j = 0; j < mods.Length; j++)
                    allMods[idx++] = mods[j];
            }

            // 3. 稳定排序：Op 优先 + Channel
            for (int i = 0; i < modCount - 1; i++)
            {
                for (int j = 0; j < modCount - 1 - i; j++)
                {
                    if (CompareModifierPriority(allMods[j], allMods[j + 1]) > 0)
                        (allMods[j], allMods[j + 1]) = (allMods[j + 1], allMods[j]);
                }
            }

            // 4. Reset → Apply
            attributeSet.Reset();
            for (int i = 0; i < modCount; i++)
            {
                ApplyModifier(attributeSet, allMods[i]);
            }
        }

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
