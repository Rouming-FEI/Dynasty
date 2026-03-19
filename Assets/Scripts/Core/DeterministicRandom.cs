namespace Core
{
    /// <summary>
    /// Deterministic pseudo-random number generator using xorshift64 algorithm.
    /// Produces identical sequences given the same seed — required for lockstep frame sync.
    /// Zero heap allocations in all methods (Hot Path safe).
    /// </summary>
    public class DeterministicRandom
    {
        private ulong _state;
        private readonly long _seed;

        /// <summary>
        /// The seed used to initialize this generator.
        /// </summary>
        public long Seed => _seed;
        public ulong State => _state;

        /// <summary>
        /// Creates a new deterministic random generator.
        /// </summary>
        /// <param name="seed">
        /// Initial seed. If 0, it is automatically set to 1
        /// (xorshift requires a non-zero state).
        /// </param>
        public DeterministicRandom(long seed)
        {
            _seed = seed;
            _state = (ulong)(seed == 0 ? 1 : seed);
        }

        /// <summary>
        /// Generates the next pseudo-random 64-bit integer using xorshift64
        /// and advances the internal state.
        /// </summary>
        public long NextLong()
        {
            ulong x = _state;
            x ^= x << 13;
            x ^= x >> 7;
            x ^= x << 17;
            _state = x;
            return (long)x;
        }

        /// <summary>
        /// Returns a non-negative pseudo-random integer in [0, maxExclusive).
        /// </summary>
        public int Next(int maxExclusive)
        {
            return (int)((ulong)NextLong() % (uint)maxExclusive);
        }

        /// <summary>
        /// Returns a pseudo-random integer in [minInclusive, maxExclusive).
        /// </summary>
        public int Next(int minInclusive, int maxExclusive)
        {
            return minInclusive + Next(maxExclusive - minInclusive);
        }

        /// <summary>
        /// Returns a pseudo-random float in [0, 1) using bits 40–63
        /// (24 bits of precision, divided by 2^24).
        /// </summary>
        public float NextFloat()
        {
            // Use the high 24 bits (40–63) for good mantissa distribution.
            return ((ulong)NextLong() >> 40) / 16777216.0f;
        }

        /// <summary>恢复随机流内部状态（快照反序列化用；state 必须来自同 seed 的实例）。</summary>
        public void RestoreState(ulong state)
        {
            if (state == 0) state = 1;   // xorshift 不允许零状态
            _state = state;
        }
    }
}
