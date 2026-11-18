using System;
using System.Collections.Generic;

namespace Game.Domain
{
    /// <summary>
    /// A merge chain: tiers 1..<see cref="MaxTier"/>. Two identical items of tier t (t &lt; MaxTier)
    /// merge into one item of tier t+1. Any tier may optionally be a generator.
    /// </summary>
    public sealed class ChainDefinition
    {
        private readonly int[] _sellValues;
        private readonly GeneratorDefinition[] _generatorTiers;

        /// <param name="id">Stable identifier referenced by saves and content data.</param>
        /// <param name="maxTier">Highest tier; items at this tier do not merge further.</param>
        /// <param name="sellValues">Coin value per tier (index 0 = tier 1). 0 means "trash for nothing".</param>
        /// <param name="generatorTiers">Optional; same length as tiers, null entries for non-generator tiers.</param>
        /// <param name="strictlyDoubling">If true, merge value of tier t is 2^(t-1) and is checked for conservation.</param>
        public ChainDefinition(string id, int maxTier, IReadOnlyList<int> sellValues,
            IReadOnlyList<GeneratorDefinition> generatorTiers = null, bool strictlyDoubling = false)
        {
            Id = id;
            MaxTier = maxTier;
            _sellValues = CopyOf(sellValues);
            _generatorTiers = CopyOf(generatorTiers);
            StrictlyDoubling = strictlyDoubling;
        }

        public string Id { get; }
        public int MaxTier { get; }
        public bool StrictlyDoubling { get; }
        public IReadOnlyList<int> SellValues => _sellValues;

        /// <summary>Raw generator tier table as configured (may be empty when the chain has no generators).</summary>
        public IReadOnlyList<GeneratorDefinition> GeneratorTiers => _generatorTiers;

        public bool IsValidTier(int tier) => tier >= 1 && tier <= MaxTier;

        public int GetSellValue(int tier) => tier >= 1 && tier <= _sellValues.Length ? _sellValues[tier - 1] : 0;

        public GeneratorDefinition GetGenerator(int tier) =>
            tier >= 1 && tier <= _generatorTiers.Length ? _generatorTiers[tier - 1] : null;

        /// <summary>Merge value of a tier for strictly doubling chains (2^(tier-1)); 0 otherwise.</summary>
        public long GetMergeValue(int tier) => StrictlyDoubling && IsValidTier(tier) ? 1L << (tier - 1) : 0L;

        private static T[] CopyOf<T>(IReadOnlyList<T> source)
        {
            if (source == null)
            {
                return Array.Empty<T>();
            }

            var copy = new T[source.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                copy[i] = source[i];
            }

            return copy;
        }
    }
}
