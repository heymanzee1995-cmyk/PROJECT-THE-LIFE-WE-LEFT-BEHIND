using System;

namespace Game.Domain
{
    /// <summary>
    /// Deterministic, platform-independent RNG (SplitMix64). All gameplay randomness must go through
    /// an instance of this class so simulations and tests are reproducible. The full state is a single
    /// 64-bit value that is persisted with the save.
    /// </summary>
    public sealed class SeededRandom
    {
        private ulong _state;

        public SeededRandom(ulong seed)
        {
            _state = seed;
        }

        /// <summary>Raw generator state. Persist this to resume the exact same sequence.</summary>
        public ulong State
        {
            get => _state;
            set => _state = value;
        }

        public ulong NextULong()
        {
            unchecked
            {
                _state += 0x9E3779B97F4A7C15UL;
                ulong z = _state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        /// <summary>Uniform integer in [0, maxExclusive). Unbiased (rejection sampling).</summary>
        public int NextInt(int maxExclusive)
        {
            if (maxExclusive <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), "maxExclusive must be positive.");
            }

            ulong bound = (ulong)maxExclusive;
            ulong threshold = (ulong.MaxValue - bound + 1) % bound; // == 2^64 mod bound
            while (true)
            {
                ulong r = NextULong();
                if (r >= threshold)
                {
                    return (int)(r % bound);
                }
            }
        }

        /// <summary>Uniform integer in [minInclusive, maxExclusive).</summary>
        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
            {
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), "maxExclusive must be greater than minInclusive.");
            }

            return minInclusive + NextInt(maxExclusive - minInclusive);
        }
    }
}
