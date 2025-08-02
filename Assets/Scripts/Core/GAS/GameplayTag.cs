using System;

namespace Core.GAS
{
    public readonly struct GameplayTag : IEquatable<GameplayTag>
    {
        public static readonly GameplayTag Invalid = new(0);

        public int Id { get; }
        public bool IsValid => Id > 0;

        public GameplayTag(int id) { Id = id; }
        public bool Equals(GameplayTag other) => Id == other.Id;
        public override bool Equals(object obj) => obj is GameplayTag other && Equals(other);
        public override int GetHashCode() => Id;
        public static bool operator ==(GameplayTag left, GameplayTag right) => left.Equals(right);
        public static bool operator !=(GameplayTag left, GameplayTag right) => !left.Equals(right);
        public override string ToString() => IsValid ? $"Tag({Id})" : "Tag(Invalid)";
    }

    public class GameplayTagContainer
    {
        internal long _mask;
        public long Mask => _mask;

        public int Count
        {
            get
            {
                ulong v = (ulong)_mask;
                v = v - ((v >> 1) & 0x5555555555555555UL);
                v = (v & 0x3333333333333333UL) + ((v >> 2) & 0x3333333333333333UL);
                v = (v + (v >> 4)) & 0x0F0F0F0F0F0F0F0FUL;
                return (int)((v * 0x0101010101010101UL) >> 56);
            }
        }

        public void Clear() { _mask = 0; }

        public bool HasTag(GameplayTag tag)
        {
            if (!tag.IsValid) return false;
            return (_mask & (1L << (tag.Id - 1))) != 0;
        }

        public bool HasAny(GameplayTagContainer other) => (_mask & other._mask) != 0;
        public bool HasAny(params GameplayTag[] tags)
        {
            for (int i = 0; i < tags.Length; i++)
                if (HasTag(tags[i])) return true;
            return false;
        }

        public bool HasAll(GameplayTagContainer other) => (_mask & other._mask) == other._mask;
        public bool HasAll(params GameplayTag[] tags)
        {
            for (int i = 0; i < tags.Length; i++)
                if (!HasTag(tags[i])) return false;
            return true;
        }

        public void AddTag(GameplayTag tag)
        {
            if (tag.IsValid) _mask |= 1L << (tag.Id - 1);
        }

        public void RemoveTag(GameplayTag tag)
        {
            if (tag.IsValid) _mask &= ~(1L << (tag.Id - 1));
        }
    }
}
