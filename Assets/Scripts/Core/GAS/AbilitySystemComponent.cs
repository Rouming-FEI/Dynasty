using Core.ObjectPool;
using Gameplay.Characters;
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

        /// <summary>精确设置属性 current 值（快照恢复用；保留引擎钳制语义）。</summary>
        public void SetAttributeCurrentValue(int attributeId, float value) =>
            _attributeSet?.SetCurrentValue(attributeId, value);

        /// <summary>读取属性 base 值（快照序列化用；GetAttributeValue 返回 current）。</summary>
        public float GetAttributeBaseValue(int attributeId) =>
            _attributeSet?.GetBaseValue(attributeId) ?? 0f;

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

            // 代行者主的庇护（NoStatus）：无法被施加任何状态（带 GrantTags 的 GE 拒绝；
            // 纯伤害 Instant GE 无 GrantTags，放行）
            if (Tags.HasTag(GameplayTagConstants.NoStatus)
                && ge.GrantTags != null && ge.GrantTags.Length > 0)
            {
                ReturnSpec(spec);
                return;
            }

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

            // 剑鬼绝症（Id=220）：周期被动负载，每回合末侵蚀 -0.5 最低 0.5；鬼化取消。
            // 绝症经 RegisterPassive 的周期被动路径（TickEffects → ApplyGameplayEffect）
            // 执行，与周期 GE 走 ApplyPeriodicPayload 的路径不同，故特例放在此处。
            if (ge.Id == 220)
            {
                if (!Tags.HasTag(GameplayTagConstants.GhostForm))
                {
                    float ero = GetAttributeValue(AttributeConstants.ErosionLimit);
                    float next = ero - 0.5f;
                    if (next < 0.5f) next = 0.5f;
                    _attributeSet.SetCurrentValue(AttributeConstants.ErosionLimit, next);
                }
                ReturnSpec(spec);
                return;
            }

            // 迷离之境心雾周期（Id=280）：周期被动负载（CharmSpiritDef Skills[2]），
            // 2 层 -0.5 理智不致死；3 层 -1 理智并退出迷离（移除 MistState + GE 210 + 层数清零）。
            // 与绝症同路径（TickEffects → ApplyGameplayEffect），故特例放在此处。
            if (ge.Id == 280)
            {
                if (!Tags.HasTag(GameplayTagConstants.MistState)) { ReturnSpec(spec); return; }
                if (_mistLayers >= 3)
                {
                    ModifyAttributeCurrentValue(AttributeConstants.Sanity, -1f);
                    float s = GetAttributeValue(AttributeConstants.Sanity);
                    if (s < 0.5f) _attributeSet.SetCurrentValue(AttributeConstants.Sanity, 0.5f);
                    Tags.RemoveTag(GameplayTagConstants.MistState);
                    ResetMistLayers();
                    // 移除迷离之境 GE（Id=210）
                    for (int i = _activeEffects.Count - 1; i >= 0; i--)
                        if (_activeEffects[i].Definition != null && _activeEffects[i].Definition.Id == 210)
                            RemoveGameplayEffect(_activeEffects[i]);
                }
                else if (_mistLayers >= 2)
                {
                    ModifyAttributeCurrentValue(AttributeConstants.Sanity, -0.5f);
                    float s = GetAttributeValue(AttributeConstants.Sanity);
                    if (s < 0.5f) _attributeSet.SetCurrentValue(AttributeConstants.Sanity, 0.5f);
                }
                ReturnSpec(spec);
                return;
            }

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

            // 静止时空 (Id=250)：结束回复 1 理智（钳 MaxSanity 由 SetCurrentValue 保证）
            if (ge != null && ge.Id == 250)
                _attributeSet.SetCurrentValue(AttributeConstants.Sanity,
                    GetAttributeValue(AttributeConstants.Sanity) + 1f);

            SendEvent(GameplayEventTypes.OnGERemoved, spec.SourceId, OwnerId, 0f);

            ReturnSpec(spec);
        }

        /// <summary>移除所有指定 Id 的活跃 GE（倒序遍历，归还模式与 RemoveGameplayEffect 一致）。</summary>
        public void RemoveEffectsById(int effectId)
        {
            for (int i = _activeEffects.Count - 1; i >= 0; i--)
            {
                GameplayEffectSpec spec = _activeEffects[i];
                if (spec.Definition != null && spec.Definition.Id == effectId)
                    RemoveGameplayEffect(spec);
            }
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

            // 周期被动：每回合递减，到期执行负载并按 Interval 重置（Dead 角色暂停）
            if (!Tags.HasTag(GameplayTagConstants.Dead))
            {
                for (int p = 0; p < _passiveCount; p++)
                {
                    if (--_passiveTimers[p] > 0) continue;
                    _passiveTimers[p] = _passiveIntervals[p];
                    GameplayEffect[] payload = _passivePayloads[p];
                    if (payload == null) continue;
                    for (int e = 0; e < payload.Length; e++)
                    {
                        GameplayEffect ge = payload[e];
                        if (ge == null) continue;
                        // 倾听者地狱的呓语（Id=260）：周期被动负载，与一人判定，不走通用 GE 挂载
                        if (ge.Id == 260)
                        {
                            ApplyWhisperPayload(state);
                            continue;
                        }
                        // 木灵瘟疫之源（Id=300）：周期被动负载，需 state 找同地点角色，不走通用 GE 挂载
                        if (ge.Id == 300)
                        {
                            ApplyPlague(state);
                            continue;
                        }
                        // 代行者孤狼（Id=330）：周期被动负载，理智 +0.5，不走通用 GE 挂载
                        if (ge.Id == 330)
                        {
                            ApplyLoneWolf();
                            continue;
                        }
                        // 代行者主的意志（Id=331）：周期被动负载，本能/侵蚀/防御联动，需 state 判伤害追踪
                        if (ge.Id == 331)
                        {
                            ApplyLoneWill(state);
                            continue;
                        }
                        // 数据幽灵分身负载（Id=341）：分身每回合末扣 1 理智；至 0 → 分身死亡判定链
                        if (ge.Id == 341)
                        {
                            ApplyGhostCloneDrain(state);
                            continue;
                        }
                        GameplayEffectSpec spec = RentSpec();
                        spec.Definition = ge;
                        spec.SourceId = OwnerId;
                        spec.TargetId = OwnerId;
                        spec.RemainingTurns = ge.DurationTurns;
                        ApplyGameplayEffect(spec);
                    }
                }
            }

            // 技能冷却递减（Dead 跳过）
            if (!Tags.HasTag(GameplayTagConstants.Dead))
            {
                for (int i = 0; i < _skillCooldownCount; i++)
                    if (_skillCooldowns[i] > 0) _skillCooldowns[i]--;
            }
        }

        /// <summary>
        /// 周期负载执行：负 Sanity Add（DoT）统一走 DamagePipeline，保证
        /// 免疫/护盾/防御/死亡结算顺序一致；其余修改器直接应用。
        /// </summary>
        private void ApplyPeriodicPayload(WorldState state, GameplayEffectSpec spec)
        {
            // 束缚（Id=310）：阵法大师束缚的周期负载，每回合末对目标 0.5 物理效果伤
            if (spec.Definition.Id == 310)
            {
                Character owner = state != null ? state.GetCharacter(OwnerId) : null;
                if (owner == null || owner.IsDead) return;
                DamagePipeline.ApplyDamage(state, -1, OwnerId, 0.5f, DamageType.Effect);
                return;
            }

            // 鬼葬（Id=211）：按所在地人数三档结算（docs/项目介绍.md 7.12）
            if (spec.Definition.Id == 211)
            {
                Character owner = state != null ? state.GetCharacter(OwnerId) : null;
                if (owner == null || owner.IsDead) return;
                int nearby = 0;
                for (int i = 0; i < state.Characters.Length; i++)
                {
                    Character ch = state.Characters[i];
                    if (ch != null && !ch.IsDead && ch.CurrentLocationId == owner.CurrentLocationId)
                        nearby++;
                }
                if (nearby == 1)
                {
                    ModifyAttributeCurrentValue(AttributeConstants.Sanity, 0.5f);
                }
                else if (nearby == 2)
                {
                    for (int i = 0; i < state.Characters.Length; i++)
                    {
                        Character ch = state.Characters[i];
                        if (ch == null || ch.IsDead || ch.Id == OwnerId) continue;
                        if (ch.CurrentLocationId == owner.CurrentLocationId)
                            DamagePipeline.ApplyDamage(state, OwnerId, ch.Id, 1f, DamageType.Effect);
                    }
                    DamagePipeline.ApplyDamage(state, OwnerId, OwnerId, 0.5f, DamageType.Effect);
                }
                else
                {
                    DamagePipeline.ApplyDamage(state, OwnerId, OwnerId, 1f, DamageType.Effect);
                    for (int i = 0; i < state.Characters.Length; i++)
                    {
                        Character ch = state.Characters[i];
                        if (ch == null || ch.IsDead || ch.Id == OwnerId) continue;
                        if (ch.CurrentLocationId == owner.CurrentLocationId)
                            DamagePipeline.ApplyDamage(state, OwnerId, ch.Id, 0.5f, DamageType.Effect);
                    }
                }
                return;
            }

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

        /// <summary>
        /// 倾听者地狱的呓语（Id=260）：周期被动负载，每 2 回合与一名存活角色判定。
        /// 胜 → 永久攻击 +0.5；败 → 侵蚀上限 -1（SetCurrentValue 钳 ≥0）。
        /// </summary>
        private void ApplyWhisperPayload(WorldState state)
        {
            if (state == null) return;
            Character target = null;
            for (int i = 0; i < state.Characters.Length; i++)
            {
                Character ch = state.Characters[i];
                if (ch != null && !ch.IsDead && ch.Id != OwnerId) { target = ch; break; }
            }
            if (target == null) return;
            if (Gameplay.Judgment.JudgmentDuel.Resolve(state, state.GetCharacter(OwnerId), target))
                ModifyAttributeCurrentValue(AttributeConstants.AttackBonus, 0.5f); // 胜：永久 +0.5
            else
                _attributeSet.SetCurrentValue(AttributeConstants.ErosionLimit,
                    GetAttributeValue(AttributeConstants.ErosionLimit) - 1f); // 败：-1（钳 ≥0）
        }

        /// <summary>
        /// 木灵瘟疫之源（Id=300）：理智非满时，同地点其他角色受 0.5 法术效果伤（不含自己）。
        /// </summary>
        private void ApplyPlague(WorldState state)
        {
            if (state == null) return;
            float sanity = GetAttributeValue(AttributeConstants.Sanity);
            float max = GetAttributeValue(AttributeConstants.MaxSanity);
            if (max > 0f && sanity >= max) return;
            Character owner = state.GetCharacter(OwnerId);
            if (owner == null) return;
            for (int i = 0; i < state.Characters.Length; i++)
            {
                Character ch = state.Characters[i];
                if (ch == null || ch.IsDead || ch.Id == OwnerId) continue;
                if (ch.CurrentLocationId == owner.CurrentLocationId)
                    DamagePipeline.ApplyDamage(state, OwnerId, ch.Id, 0.5f, DamageType.Effect);
            }
        }

        /// <summary>
        /// 代行者孤狼（Id=330）：周期被动负载，理智 +0.5。
        /// </summary>
        private void ApplyLoneWolf()
        {
            ModifyAttributeCurrentValue(AttributeConstants.Sanity, 0.5f);
        }

        /// <summary>
        /// 代行者主的意志（Id=331）：周期被动负载。
        /// 本能 ≥4 进入审判者模式（<4 退出）；每 3 回合未造成伤害则本能 +1；
        /// 侵蚀上限 -0.5、物理防御 +0.5（SetAttributeBaseValue 同时写 base+current，
        /// 派生防御由 BuffResolver 下次 Resolve 以 base 重建，故 base 写入语义正确）。
        /// </summary>
        private void ApplyLoneWill(WorldState state)
        {
            // 审判者模式（本能 ≥4）：间隔 2；否则 3（PassiveInterval 已设 3，审判者由 TickEffects 特判）
            if (_instinct >= 4 && !Tags.HasTag(GameplayTagConstants.JudgeMode))
                Tags.AddTag(GameplayTagConstants.JudgeMode);
            if (_instinct < 4 && Tags.HasTag(GameplayTagConstants.JudgeMode))
                Tags.RemoveTag(GameplayTagConstants.JudgeMode);

            // 「每 3 回合未造成伤害」→ 周期负载内检查距上次造成伤害的回合差
            if (state == null || state.CurrentTurn - _lastDamageTurn >= 3)
                ModifyInstinct(1);

            SetAttributeBaseValue(AttributeConstants.ErosionLimit,
                GetAttributeValue(AttributeConstants.ErosionLimit) - 0.5f);
            SetAttributeBaseValue(AttributeConstants.PhysicalDefense,
                GetAttributeValue(AttributeConstants.PhysicalDefense) + 0.5f);
        }

        /// <summary>
        /// 数据幽灵分身负载（Id=341，周期被动 129 的载体，S5）：本体回合末分身扣 1 理智。
        /// 无分身零开销返回；本体死亡时由 DeathCommand 清理分身（此处 IsDead 防御双清）。
        /// 归 0 → 分身死亡判定链（本体 vs 数组序第一存活他人，败 → 本体 DeathCommand，EndTurn 统一执行）。
        /// </summary>
        private void ApplyGhostCloneDrain(WorldState state)
        {
            if (state == null) return;
            Character owner = state.GetCharacter(OwnerId);
            if (owner == null || owner.IsDead) return;
            GhostAbyss.DrainClone(state, owner);
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
        // 心雾层数 — 迷离之境计数器（跨回合保留，不随 GE 过期）
        // ---------------------------------------------------------------------

        private int _mistLayers;

        public int GetMistLayers() => _mistLayers;
        public void AddMistLayer() => _mistLayers++;
        public void ResetMistLayers() => _mistLayers = 0;

        // ---------------------------------------------------------------------
        // 阵法大师阵盘 — 血炼计数器（跨回合保留，不随 GE 过期）
        // ---------------------------------------------------------------------

        private int _bloodSeals;

        public int GetBloodSeals() => _bloodSeals;
        public void SetBloodSeals(int v) => _bloodSeals = v;
        public void ModifyBloodSeals(int delta) { _bloodSeals += delta; if (_bloodSeals < 0) _bloodSeals = 0; }

        // ---------------------------------------------------------------------
        // 代行者本能 — 主的意志计数器（跨回合保留，不随 GE 过期）
        // ---------------------------------------------------------------------

        private int _instinct;
        public int GetInstinct() => _instinct;
        public void ModifyInstinct(int delta) { _instinct += delta; if (_instinct < 0) _instinct = 0; }

        // 最近造成伤害的回合（主的意志判定用；-999 = 从未造成伤害）
        private int _lastDamageTurn = -999;
        public void MarkDamageDealt(int turn) => _lastDamageTurn = turn;

        // ---------------------------------------------------------------------
        // 周期被动计时器 — PassiveInterval / PassiveEffects
        // ---------------------------------------------------------------------

        // 周期被动计时器：固定上限栈数组，零 GC
        private GameplayEffect[][] _passivePayloads = new GameplayEffect[4][];
        private int[] _passiveTimers = new int[4];
        private int[] _passiveIntervals = new int[4];
        private int _passiveCount;

        /// <summary>注册周期被动（角色创建时由 TurnManager 调用）。</summary>
        public void RegisterPassive(SkillDef skill)
        {
            if (skill == null || skill.PassiveInterval <= 0) return;
            if (_passiveCount >= _passiveTimers.Length) return;
            _passivePayloads[_passiveCount] = skill.PassiveEffects;
            _passiveIntervals[_passiveCount] = skill.PassiveInterval;
            _passiveTimers[_passiveCount] = skill.PassiveInterval;
            _passiveCount++;
        }

        // ---------------------------------------------------------------------
        // 技能冷却 — CooldownTurns 按技能索引存储剩余回合
        // ---------------------------------------------------------------------

        // 技能冷却：按技能索引存储剩余冷却回合（与 CharacterDef.Skills 平行）
        private int[] _skillCooldowns = new int[8];
        private int _skillCooldownCount;

        public void EnsureCooldownSlots(int skillCount)
        {
            if (skillCount > _skillCooldowns.Length)
                System.Array.Resize(ref _skillCooldowns, skillCount);
            _skillCooldownCount = skillCount;
        }

        public int GetCooldown(int skillIndex) =>
            skillIndex >= 0 && skillIndex < _skillCooldowns.Length ? _skillCooldowns[skillIndex] : 0;

        public void SetCooldown(int skillIndex, int turns)
        {
            if (skillIndex >= 0 && skillIndex < _skillCooldowns.Length)
                _skillCooldowns[skillIndex] = turns;
        }

        // ---------------------------------------------------------------------
        // Queries
        // ---------------------------------------------------------------------

        public bool IsDead   => Tags.HasTag(GameplayTagConstants.Dead);
        public bool CanAct   => !IsDead && !Tags.HasTag(GameplayTagConstants.CannotAct);
        public bool CanMove  => CanAct && !Tags.HasTag(GameplayTagConstants.CannotMove);

        /// <summary>是否存在指定 Id 的活跃 GE（验收/调试工具用，非热路径）。</summary>
        public bool HasActiveEffect(int geId)
        {
            for (int i = 0; i < _activeEffects.Count; i++)
                if (_activeEffects[i].Definition != null && _activeEffects[i].Definition.Id == geId)
                    return true;
            return false;
        }

        // ---- 快照恢复访问器（序列化/反序列化专用，非热路径）----

        /// <summary>精确设置心雾层数（快照恢复用；AddMistLayer 只能递增）。</summary>
        public void SetMistLayers(int layers) { _mistLayers = layers < 0 ? 0 : layers; }

        /// <summary>覆盖周期被动计时器（快照恢复用；timers 长度须等于已注册被动数，null 安全忽略）。</summary>
        public void RestorePassiveTimers(int[] timers)
        {
            if (timers == null) return;
            for (int i = 0; i < timers.Length && i < _passiveCount; i++)
                _passiveTimers[i] = timers[i];
        }

        /// <summary>读取周期被动计时器（快照序列化用）。</summary>
        public int GetPassiveTimer(int index) =>
            index >= 0 && index < _passiveCount ? _passiveTimers[index] : -1;

        /// <summary>按索引读取活跃 GE（快照序列化用；配合 ActiveEffectCount）。</summary>
        public GameplayEffectSpec GetActiveEffect(int index) =>
            index >= 0 && index < _activeEffects.Count ? _activeEffects[index] : null;

        /// <summary>直接插入活跃 GE（快照恢复用；不经 ApplyGameplayEffect，避免叠加/事件副作用）。</summary>
        public void RestoreActiveEffect(GameplayEffectSpec spec)
        {
            if (spec != null && spec.Definition != null) _activeEffects.Add(spec);
        }

        /// <summary>周期被动数量（快照序列化用；配合 GetPassiveTimer）。</summary>
        public int PassiveCount => _passiveCount;

        /// <summary>技能冷却槽数量（快照序列化用；配合 GetCooldown）。</summary>
        public int CooldownSlotCount => _skillCooldownCount;

        /// <summary>最近造成伤害的回合（快照序列化用；-999 = 从未造成伤害）。</summary>
        public int GetLastDamageTurn() => _lastDamageTurn;

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
