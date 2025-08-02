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
        /// </summary>
        public void ApplyGameplayEffect(GameplayEffectSpec spec)
        {
            if (spec?.Definition == null) return;
            GameplayEffect ge = spec.Definition;

            // --- Tag condition check ---
            if (ge.ApplicationRequiredTags != null && ge.ApplicationRequiredTags.Length > 0)
            {
                for (int i = 0; i < ge.ApplicationRequiredTags.Length; i++)
                {
                    if (!Tags.HasTag(ge.ApplicationRequiredTags[i]))
                        return;
                }
            }

            if (ge.ApplicationBlockedTags != null && ge.ApplicationBlockedTags.Length > 0)
            {
                for (int i = 0; i < ge.ApplicationBlockedTags.Length; i++)
                {
                    if (Tags.HasTag(ge.ApplicationBlockedTags[i]))
                        return;
                }
            }

            // --- Apply attribute modifiers ---
            if (ge.Modifiers != null)
            {
                float mult = spec.MagnitudeMultiplier;
                for (int i = 0; i < ge.Modifiers.Length; i++)
                {
                    AttributeModifier mod = ge.Modifiers[i];
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
            if (!ge.IsInstant && ge.DurationTurns > 0)
                _activeEffects.Add(spec);

            // --- Internal event ---
            SendEvent(GameplayEventTypes.OnGEApplied, spec.SourceId, OwnerId,
                spec.MagnitudeMultiplier);
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

            spec.Reset();
            _specPool.Return(spec);
        }

        // ---------------------------------------------------------------------
        // Tick — called each turn end
        // ---------------------------------------------------------------------

        /// <summary>
        /// Tick all active GameplayEffects. Expired effects are removed.
        /// Iterates in reverse so removal does not invalidate indices.
        /// </summary>
        public void TickEffects()
        {
            for (int i = _activeEffects.Count - 1; i >= 0; i--)
            {
                GameplayEffectSpec spec = _activeEffects[i];
                spec.Tick();
                if (spec.IsExpired)
                    RemoveGameplayEffect(spec);
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
        // Queries
        // ---------------------------------------------------------------------

        public bool IsDead   => Tags.HasTag(GameplayTagConstants.Dead);
        public bool CanAct   => !IsDead && !Tags.HasTag(GameplayTagConstants.CannotAct);
        public bool CanMove  => CanAct && !Tags.HasTag(GameplayTagConstants.CannotMove);
    }
}
