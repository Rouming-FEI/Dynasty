using Core.ObjectPool;
namespace Core.GAS
{
    using System.Collections.Generic;

    /// <summary>
    /// Ability System Component — central hub for applying GameplayEffects, managing
    /// attribute modifications, tag state, and internal event dispatching.
    ///
    /// ApplyGameplayEffect is the primary Hot Path and must remain zero-GC (no heap
    /// allocations, no boxing, no LINQ). All loops use 'for', GameplayEffectSpec
    /// instances are pooled via GenericPool, and GameplayEvent is a stack-allocated struct.
    /// </summary>
    public class AbilitySystemComponent
    {
        public int OwnerId { get; private set; }

        private AttributeSet _attributeSet;
        private readonly List<GameplayEffectSpec> _activeEffects;
        public GameplayTagContainer Tags { get; private set; }

        private readonly GameplayEvent[] _eventBuffer;
        private int _eventCount;

        private readonly GenericPool<GameplayEffectSpec> _specPool;

        public AbilitySystemComponent(int ownerId, int initialSpecPoolSize = 16)
        {
            OwnerId = ownerId;
            Tags = new GameplayTagContainer();
            _activeEffects = new List<GameplayEffectSpec>(8);
            _eventBuffer = new GameplayEvent[32];
            _eventCount = 0;
            _specPool = new GenericPool<GameplayEffectSpec>(initialSpecPoolSize);
            _specPool.Prewarm(initialSpecPoolSize);
        }

        // ---------------------------------------------------------------------
        // AttributeSet management
        // ---------------------------------------------------------------------

        public void SetAttributeSet(AttributeSet attributeSet) => _attributeSet = attributeSet;

        public float GetAttributeValue(int attributeId) =>
            _attributeSet?.GetValue(attributeId) ?? 0f;

        public void SetAttributeBaseValue(int attributeId, float value) =>
            _attributeSet?.SetBaseValue(attributeId, value);

        /// <summary>Delta-modify current value (positive = increase, negative = decrease).</summary>
        public void ModifyAttributeCurrentValue(int attributeId, float delta)
        {
            if (_attributeSet == null) return;
            float current = _attributeSet.GetValue(attributeId);
            _attributeSet.SetCurrentValue(attributeId, current + delta);
        }

        // ---------------------------------------------------------------------
        // CORE HOT PATH — ApplyGameplayEffect (zero allocations)
        // ---------------------------------------------------------------------

        /// <summary>
        /// Apply a GameplayEffectSpec to this component.
        /// Flow: tag condition check → modifier execution → tag mutation →
        /// mount duration effects → send internal event.
        /// Takes ownership of the spec: any spec not mounted into the active
        /// list (Instant, zero-duration, or rejected by tags) is returned to
        /// the pool before this method exits.
        /// </summary>
        public void ApplyGameplayEffect(GameplayEffectSpec spec)
        {
            if (spec == null) return;
            if (spec.Definition == null)
            {
                ReturnSpec(spec);
                return;
            }
            GameplayEffect ge = spec.Definition;

            // --- Tag condition check ---
            if (ge.ApplicationRequiredTags != null && ge.ApplicationRequiredTags.Length > 0)
            {
                for (int i = 0; i < ge.ApplicationRequiredTags.Length; i++)
                {
                    if (!Tags.HasTag(ge.ApplicationRequiredTags[i]))
                    {
                        ReturnSpec(spec);
                        return;
                    }
                }
            }

            if (ge.ApplicationBlockedTags != null && ge.ApplicationBlockedTags.Length > 0)
            {
                for (int i = 0; i < ge.ApplicationBlockedTags.Length; i++)
                {
                    if (Tags.HasTag(ge.ApplicationBlockedTags[i]))
                    {
                        ReturnSpec(spec);
                        return;
                    }
                }
            }

            // --- Apply attribute modifiers ---
            // 周期型 GE 的 Modifier 是周期负载，由每回合 ApplyPeriodicPayload 执行；
            // 挂载时不再立即执行，避免 HoT/DoT 多计算一次。
            if (ge.Modifiers != null && !ge.Period.HasValue)
                ApplyModifiers(ge.Modifiers, spec.MagnitudeMultiplier);

            // --- Apply tag mutations ---
            if (ge.GrantTags != null)
            {
                for (int i = 0; i < ge.GrantTags.Length; i++)
                    Tags.AddTag(ge.GrantTags[i]);
            }

            if (ge.RemoveTags != null)
            {
                for (int i = 0; i < ge.RemoveTags.Length; i++)
                    Tags.RemoveTag(ge.RemoveTags[i]);
            }

            // --- Mount non-instant duration effects ---
            bool mounted = !ge.IsInstant && ge.DurationTurns > 0;
            if (mounted)
            {
                // 挂载即初始化剩余回合数（单一事实来源）：Reset() 会把 RemainingTurns
                // 清零，若不在此赋值，IsExpired (RemainingTurns <= 0) 会在首次 Tick
                // 就移除该效果，与 DurationTurns > 0 的挂载判断脱节。
                spec.RemainingTurns = ge.DurationTurns;
                // 周期效果的首个间隔从挂载后开始计数：PeriodTick 初始化为 Interval，
                // 否则 Reset 后的 0 会让周期负载在挂载后的第一次 Tick 立即触发。
                if (ge.Period.HasValue)
                    spec.PeriodTick = ge.Period.Value.Interval;
                _activeEffects.Add(spec);
            }

            // --- Internal event ---
            SendEvent(GameplayEventTypes.OnGEApplied, spec.SourceId, OwnerId,
                spec.MagnitudeMultiplier);

            // --- Recycle unmounted specs (Instant / zero-duration) ---
            // They never enter _activeEffects, so RemoveGameplayEffect (the only
            // other return path) can never reclaim them.
            if (!mounted)
                ReturnSpec(spec);
        }

        /// <summary>
        /// Execute a modifier array against current values. Zero-GC.
        /// </summary>
        private void ApplyModifiers(AttributeModifier[] modifiers, float mult)
        {
            for (int i = 0; i < modifiers.Length; i++)
                ApplySingleModifier(modifiers[i], mult);
        }

        private void ApplySingleModifier(AttributeModifier mod, float mult)
        {
            float mag = mod.Magnitude * mult;

            switch (mod.Op)
            {
                case AttributeModifierOp.Add:
                    ModifyAttributeCurrentValue(mod.AttributeId, mag);
                    break;

                case AttributeModifierOp.Override:
                    _attributeSet?.SetCurrentValue(mod.AttributeId, mag);
                    break;

                case AttributeModifierOp.Multiply:
                    float cur = GetAttributeValue(mod.AttributeId);
                    _attributeSet?.SetCurrentValue(mod.AttributeId, cur * (1f + mag));
                    break;
            }
        }

        private void ReturnSpec(GameplayEffectSpec spec)
        {
            spec.Reset();
            _specPool.Return(spec);
        }

        // ---------------------------------------------------------------------
        // Remove GameplayEffect
        // ---------------------------------------------------------------------

        /// <summary>
        /// Remove a previously applied GameplayEffect. Reverses tag mutations,
        /// sends OnGERemoved event, and returns the spec to the pool.
        /// </summary>
        public void RemoveGameplayEffect(GameplayEffectSpec spec)
        {
            if (spec == null) return;

            _activeEffects.Remove(spec);
            GameplayEffect ge = spec.Definition;

            // Reverse tag mutations: RemoveTags that were removed are re-added,
            // GrantTags that were granted are removed.
            if (ge?.RemoveTags != null)
            {
                for (int i = 0; i < ge.RemoveTags.Length; i++)
                    Tags.AddTag(ge.RemoveTags[i]);
            }

            if (ge?.GrantTags != null)
            {
                for (int i = 0; i < ge.GrantTags.Length; i++)
                    Tags.RemoveTag(ge.GrantTags[i]);
            }

            SendEvent(GameplayEventTypes.OnGERemoved, spec.SourceId, OwnerId, 0f);

            ReturnSpec(spec);
        }

        // ---------------------------------------------------------------------
        // Tick — called each turn end
        // ---------------------------------------------------------------------

        /// <summary>
        /// Tick all active GameplayEffects. Periodic effects that fire this turn
        /// re-execute their payload (DoT/HoT). Negative Sanity payloads route
        /// through DamagePipeline so immunity/shield/defense/death checks apply.
        /// Expired effects are removed. Iterates in reverse so removal does not
        /// invalidate indices. BuffResolver then rebuilds derived attributes —
        /// always, even when no effect survives, so an expired buff's modifiers
        /// do not linger.
        /// </summary>
        public void TickEffects(WorldState state)
        {
            for (int i = _activeEffects.Count - 1; i >= 0; i--)
            {
                GameplayEffectSpec spec = _activeEffects[i];
                bool periodicFired = spec.Tick();

                // Periodic payload — Tick() only returns true when Definition has a Period.
                if (periodicFired && spec.Definition?.Modifiers != null)
                    ApplyPeriodicPayload(state, spec);

                // RemoveOnCondition：声明条件标签，任一缺失即移除（开关类 GE）
                if (spec.Definition.ExpiryPolicy == GameplayEffectExpiryPolicy.RemoveOnCondition
                    && spec.Definition.ConditionTags != null)
                {
                    bool condMet = true;
                    GameplayTag[] condTags = spec.Definition.ConditionTags;
                    for (int t = 0; t < condTags.Length; t++)
                    {
                        if (!Tags.HasTag(condTags[t])) { condMet = false; break; }
                    }
                    if (!condMet) { RemoveGameplayEffect(spec); continue; }
                }

                if (spec.IsExpired)
                    RemoveGameplayEffect(spec);
            }

            if (_attributeSet != null)
                BuffResolver.Resolve(_activeEffects, _attributeSet);
        }

        /// <summary>
        /// 周期负载执行：负 Sanity Add（DoT）统一走 DamagePipeline，保证
        /// 免疫/护盾/防御/死亡结算顺序一致；其余修改器直接应用。
        /// </summary>
        private void ApplyPeriodicPayload(WorldState state, GameplayEffectSpec spec)
        {
            AttributeModifier[] modifiers = spec.Definition.Modifiers;
            for (int i = 0; i < modifiers.Length; i++)
            {
                AttributeModifier mod = modifiers[i];
                float mag = mod.Magnitude * spec.MagnitudeMultiplier;

                if (state != null
                    && mod.AttributeId == AttributeConstants.Sanity
                    && mod.Op == AttributeModifierOp.Add
                    && mag < 0f)
                {
                    DamagePipeline.ApplyDamage(state, spec.SourceId, OwnerId, -mag, DamageType.Effect);
                    continue;
                }

                ApplySingleModifier(mod, spec.MagnitudeMultiplier);
            }
        }

        // ---------------------------------------------------------------------
        // Spec pooling
        // ---------------------------------------------------------------------

        public int ActiveEffectCount => _activeEffects.Count;

        /// <summary>
        /// Rent a spec from the pool. Caller must fill Definition, SourceId,
        /// TargetId, RemainingTurns etc. before passing to ApplyGameplayEffect.
        /// </summary>
        public GameplayEffectSpec RentSpec()
        {
            GameplayEffectSpec s = _specPool.Rent();
            s.MagnitudeMultiplier = 1f;
            return s;
        }

        // ---------------------------------------------------------------------
        // Internal event queue
        // ---------------------------------------------------------------------

        /// <summary>
        /// Enqueue an internal event into the ring buffer. Drops events silently
        /// if the buffer is full (32 events per tick — sufficient for normal play).
        /// </summary>
        public void SendEvent(int eventType, int sourceId, int targetId, float magnitude = 0f)
        {
            if (_eventCount >= _eventBuffer.Length) return;
            _eventBuffer[_eventCount++] = new GameplayEvent(eventType, sourceId, targetId)
                { Magnitude = magnitude };
        }

        /// <summary>
        /// Consume all buffered events. OnDeath → add Dead tag.
        /// Called once per tick after all GE application is complete.
        /// </summary>
        public void ProcessEvents()
        {
            for (int i = 0; i < _eventCount; i++)
            {
                ref GameplayEvent e = ref _eventBuffer[i];
                if (e.EventType == GameplayEventTypes.OnDeath)
                    Tags.AddTag(GameplayTagConstants.Dead);
            }
            _eventCount = 0;
        }

        // ---------------------------------------------------------------------
        // Guardian charge — 守护技能充能计数（跨回合保留，不随 GE 过期）
        // ---------------------------------------------------------------------

        private int _guardianCharge;

        public void AddGuardianCharge() => _guardianCharge++;
        public int GetGuardianCharge() => _guardianCharge;
        public void ResetGuardianCharge() => _guardianCharge = 0;
        /// <summary>Delta 修改充能计数（正加负减），钳位到 >= 0。</summary>
        public void ModifyGuardianCharge(int delta)
        {
            _guardianCharge += delta;
            if (_guardianCharge < 0) _guardianCharge = 0;
        }

        // ---------------------------------------------------------------------
        // Queries
        // ---------------------------------------------------------------------

        public bool IsDead   => Tags.HasTag(GameplayTagConstants.Dead);
        public bool CanAct   => !IsDead && !Tags.HasTag(GameplayTagConstants.CannotAct);
        public bool CanMove  => CanAct && !Tags.HasTag(GameplayTagConstants.CannotMove);

        // ---------------------------------------------------------------------
        // Determinism hash — 确定性自检
        // ---------------------------------------------------------------------

        /// <summary>
        /// 计算活跃 GE 列表的状态 hash。遍历每个 spec 的关键字段累加，zero-GC。
        /// </summary>
        public long ComputeEffectsHash()
        {
            long h = 0;
            for (int i = 0; i < _activeEffects.Count; i++)
            {
                GameplayEffectSpec s = _activeEffects[i];
                h = h * 31 + (s.Definition?.Id ?? 0);
                h = h * 31 + s.RemainingTurns;
                h = h * 31 + s.PeriodTick;
                h = h * 31 + s.PeriodRunCount;
                h = h * 31 + s.StackCount;
            }
            return h;
        }
    }
}
