using System;

namespace Core.GAS
{
    /// <summary>
    /// Abstract base class for attribute sets (HP, Sanity, ActionPoints, etc.).
    /// Stores base and current values in flat arrays for zero-GC access.
    /// Attribute IDs are 1-based, matching <see cref="AttributeConstants"/>.
    /// </summary>
    public abstract class AttributeSet
    {
        protected readonly float[] _baseValues;
        protected readonly float[] _currentValues;
        protected readonly int _attributeCount;

        protected AttributeSet(int attributeCount)
        {
            _attributeCount = attributeCount;
            _baseValues = new float[attributeCount];
            _currentValues = new float[attributeCount];
        }

        /// <summary>
        /// Get the current value for a 1-based attribute ID.
        /// </summary>
        public float GetValue(int attributeId)
        {
            int index = attributeId - 1;
            if (index < 0 || index >= _attributeCount)
                return 0f;
            return _currentValues[index];
        }

        /// <summary>
        /// Sets both base and current values to the given value.
        /// </summary>
        public void SetBaseValue(int attributeId, float value)
        {
            int index = attributeId - 1;
            if (index < 0 || index >= _attributeCount)
                return;
            _baseValues[index] = value;
            _currentValues[index] = value;
        }

        /// <summary>
        /// Sets only the current value, clamped to >= 0. Sanity is additionally
        /// clamped to MaxSanity so healing cannot overfill (skipped while
        /// MaxSanity is still uninitialised at 0).
        /// </summary>
        public void SetCurrentValue(int attributeId, float value)
        {
            int index = attributeId - 1;
            if (index < 0 || index >= _attributeCount)
                return;
            if (value < 0f)
                value = 0f;
            if (attributeId == AttributeConstants.Sanity)
            {
                float max = GetValue(AttributeConstants.MaxSanity);
                if (max > 0f && value > max)
                    value = max;
            }
            _currentValues[index] = value;
        }

        /// <summary>
        /// Copies all base values to current values. Used for resetting or
        /// reinitialising attribute state (e.g. at the start of a turn).
        /// WARNING: wipes persistent resources (Sanity, ActionPoints) too —
        /// only use for full reinitialisation, never for per-turn buff resolve.
        /// </summary>
        public void Reset()
        {
            Array.Copy(_baseValues, _currentValues, _attributeCount);
        }

        /// <summary>
        /// Copies the base value to the current value for a single attribute.
        /// Used by BuffResolver to rebuild derived attributes each turn
        /// without touching persistent resources.
        /// </summary>
        public void ResetAttribute(int attributeId)
        {
            int index = attributeId - 1;
            if (index < 0 || index >= _attributeCount)
                return;
            _currentValues[index] = _baseValues[index];
        }

        /// <summary>
        /// P0 placeholder: clamps the current value at the given index to >= 0.
        /// Full recalculate (e.g. applying modifiers from GameplayEffects) will
        /// be implemented when the GE system lands.
        /// </summary>
        public void Recalculate(int attributeId)
        {
            int index = attributeId - 1;
            if (index < 0 || index >= _attributeCount)
                return;
            if (_currentValues[index] < 0f)
                _currentValues[index] = 0f;
        }
    }
}
