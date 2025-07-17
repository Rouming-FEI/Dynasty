using System;
using System.Numerics;

namespace Dynasty.Core
{
    /// <summary>
    /// 不可变标签标识符。Id 为 0 表示无效标签。
    /// </summary>
    public readonly struct GameplayTag : IEquatable<GameplayTag>
    {
        public static readonly GameplayTag Invalid = new(0);

        public int Id { get; }

        public bool IsValid => Id > 0;

        public GameplayTag(int id)
        {
            Id = id;
        }

        public bool Equals(GameplayTag other) => Id == other.Id;

        public override bool Equals(object obj) => obj is GameplayTag other && Equals(other);

        public override int GetHashCode() => Id;

        public static bool operator ==(GameplayTag left, GameplayTag right) => left.Equals(right);

        public static bool operator !=(GameplayTag left, GameplayTag right) => !left.Equals(right);

        public override string ToString() => IsValid ? $"Tag({Id})" : "Tag(Invalid)";
    }

    /// <summary>
    /// 标签容器，使用 long 位掩码存储最多 64 个标签。
    /// 所有操作均为 O(1) 且零堆内存分配。
    /// </summary>
    public class GameplayTagContainer
    {
        internal long _mask;

        public long Mask => _mask;

        public int Count => BitOperations.PopCount((ulong)_mask);

        public void Clear()
        {
            _mask = 0;
        }

        public bool HasTag(GameplayTag tag)
        {
            if (!tag.IsValid) return false;
            long bit = 1L << (tag.Id - 1);
            return (_mask & bit) != 0;
        }

        public bool HasAny(GameplayTagContainer other)
        {
            return (_mask & other._mask) != 0;
        }

        public bool HasAll(GameplayTagContainer other)
        {
            return (_mask & other._mask) == other._mask;
        }

        public void AddTag(GameplayTag tag)
        {
            if (!tag.IsValid) return;
            _mask |= 1L << (tag.Id - 1);
        }

        public void RemoveTag(GameplayTag tag)
        {
            if (!tag.IsValid) return;
            _mask &= ~(1L << (tag.Id - 1));
        }
    }
}
