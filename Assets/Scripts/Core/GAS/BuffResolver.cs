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
        /// <summary>stackalloc 缓冲上限；超出部分截断，防止堆叠异常导致栈溢出。</summary>
        private const int MaxModifierCount = 64;

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

            // 2.5 封顶：stackalloc 不可无界，超过上限截断并告警（异常分支，允许分配）
            if (modCount > MaxModifierCount)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[BuffResolver] derived modifier count {modCount} exceeds cap {MaxModifierCount}, truncating");
                modCount = MaxModifierCount;
            }

            // 3. 展开到 span（栈分配），跳过非派生属性的 modifier；
            //    按 Spec 的 MagnitudeMultiplier 与 StackCount 缩放 Magnitude
            Span<AttributeModifier> allMods = stackalloc AttributeModifier[modCount];
            int idx = 0;
            for (int i = 0; i < activeEffects.Count && idx < modCount; i++)
            {
                GameplayEffectSpec spec = activeEffects[i];
                AttributeModifier[] mods = spec?.Definition?.Modifiers;
                if (mods == null) continue;
                float scale = spec.MagnitudeMultiplier * spec.StackCount;
                for (int j = 0; j < mods.Length && idx < modCount; j++)
                {
                    if (IsDerivedAttribute(mods[j].AttributeId))
                        allMods[idx++] = new AttributeModifier(
                            mods[j].AttributeId, mods[j].Op,
                            mods[j].Magnitude * scale, mods[j].Channel, mods[j].DependsOn);
                }
            }

            // 4. 排序：有 DependsOn 声明 → Kahn 拓扑；否则优先级排序
            if (HasDependency(allMods, idx))
                TopoSort(ref allMods, idx);
            else
                PrioritySort(ref allMods, idx);

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

        private static bool HasDependency(Span<AttributeModifier> mods, int count)
        {
            for (int i = 0; i < count; i++)
                if (mods[i].DependsOn != 0) return true;
            return false;
        }

        /// <summary>优先级排序（无依赖时沿用旧行为）：Op 优先 + Channel 升序。</summary>
        private static void PrioritySort(ref Span<AttributeModifier> mods, int count)
        {
            for (int i = 0; i < count - 1; i++)
                for (int j = 0; j < count - 1 - i; j++)
                    if (CompareModifierPriority(mods[j], mods[j + 1]) > 0)
                        (mods[j], mods[j + 1]) = (mods[j + 1], mods[j]);
        }

        /// <summary>
        /// Kahn 拓扑排序：A.DependsOn == B.Channel 表示 A 依赖 B（B 先结算）。
        /// 确定性：零入度节点按挂载顺序线性扫描选取；循环残留按挂载顺序裁决。
        /// </summary>
        private static void TopoSort(ref Span<AttributeModifier> mods, int count)
        {
            Span<int> indegree = stackalloc int[count];
            Span<int> order = stackalloc int[count];
            Span<int> resolved = stackalloc int[count];
            for (int i = 0; i < count; i++)
            {
                indegree[i] = 0;
                resolved[i] = 0;
                order[i] = -1;
            }

            // 建入度：i 依赖 j（DependsOn == Channel[j]）
            for (int i = 0; i < count; i++)
            {
                int dep = mods[i].DependsOn;
                if (dep == 0) continue;
                for (int j = 0; j < count; j++)
                {
                    if (i != j && mods[j].Channel == dep) indegree[i]++;
                }
            }

            int outCount = 0;
            while (outCount < count)
            {
                int pick = -1;
                for (int i = 0; i < count; i++) // 挂载顺序线性扫描：确定性
                {
                    if (resolved[i] == 0 && indegree[i] == 0) { pick = i; break; }
                }
                if (pick < 0)
                {
                    // 循环依赖：取挂载顺序第一个未解决节点裁决断环
                    for (int i = 0; i < count; i++)
                    {
                        if (resolved[i] == 0) { pick = i; break; }
                    }
                    System.Diagnostics.Debug.WriteLine("[BuffResolver] circular dependency detected, deterministic fallback applied");
                }
                order[outCount++] = pick;
                resolved[pick] = 1;
                for (int i = 0; i < count; i++)
                {
                    int dep = mods[i].DependsOn;
                    if (dep == 0) continue; // 无依赖节点不参与减入度
                    if (dep == mods[pick].Channel) indegree[i]--;
                }
            }

            Span<AttributeModifier> sorted = stackalloc AttributeModifier[count];
            for (int i = 0; i < count; i++)
                sorted[i] = mods[order[i]];
            for (int i = 0; i < count; i++)
                mods[i] = sorted[i];
        }

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
