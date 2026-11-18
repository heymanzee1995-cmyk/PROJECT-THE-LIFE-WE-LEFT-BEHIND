using System;
using System.Collections.Generic;
using Game.Domain;

namespace Game.Data
{
    /// <summary>Presentation-facing facts about item types that the engine does not need: names and colours.</summary>
    public sealed class ItemCatalog
    {
        private readonly Dictionary<string, ChainInfo> _chains = new Dictionary<string, ChainInfo>(StringComparer.Ordinal);

        internal void Add(string chainId, string color, string[] tierNames) =>
            _chains[chainId] = new ChainInfo(color, tierNames);

        /// <summary>Display name of a tier, e.g. "Teapot"; falls back to "chain 3".</summary>
        public string GetName(ItemType type)
        {
            if (type.ChainId != null && _chains.TryGetValue(type.ChainId, out ChainInfo info) &&
                info.TierNames != null && type.Tier >= 1 && type.Tier <= info.TierNames.Length &&
                !string.IsNullOrEmpty(info.TierNames[type.Tier - 1]))
            {
                return info.TierNames[type.Tier - 1];
            }

            return type.ChainId + " " + type.Tier;
        }

        /// <summary>"#RRGGBB" placeholder colour of the chain, or null.</summary>
        public string GetColor(string chainId) =>
            chainId != null && _chains.TryGetValue(chainId, out ChainInfo info) ? info.Color : null;

        private readonly struct ChainInfo
        {
            public readonly string Color;
            public readonly string[] TierNames;

            public ChainInfo(string color, string[] tierNames)
            {
                Color = color;
                TierNames = tierNames;
            }
        }
    }

    /// <summary>Everything built from one content file.</summary>
    public sealed class MergeContent
    {
        public MergeContent(EngineConfig config, IReadOnlyList<InitialPlacement> startingLayout, ItemCatalog catalog)
        {
            Config = config;
            StartingLayout = startingLayout;
            Catalog = catalog;
        }

        public EngineConfig Config { get; }
        public IReadOnlyList<InitialPlacement> StartingLayout { get; }
        public ItemCatalog Catalog { get; }
    }
}
