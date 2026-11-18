using System;
using System.Collections.Generic;

namespace Game.Domain
{
    /// <summary>Immutable, validated description of everything the merge engine needs to know about content.</summary>
    public sealed class EngineConfig
    {
        private readonly ChainDefinition[] _chains;
        private readonly Dictionary<string, ChainDefinition> _chainsById;

        public EngineConfig(BoardConfig board, IReadOnlyList<ChainDefinition> chains)
        {
            Board = board;
            _chains = new ChainDefinition[chains == null ? 0 : chains.Count];
            _chainsById = new Dictionary<string, ChainDefinition>(StringComparer.Ordinal);
            for (int i = 0; i < _chains.Length; i++)
            {
                _chains[i] = chains[i];
                if (_chains[i] != null && _chains[i].Id != null && !_chainsById.ContainsKey(_chains[i].Id))
                {
                    _chainsById.Add(_chains[i].Id, _chains[i]);
                }
            }
        }

        public BoardConfig Board { get; }
        public IReadOnlyList<ChainDefinition> Chains => _chains;

        public ChainDefinition GetChain(string chainId)
        {
            if (chainId == null)
            {
                return null;
            }

            _chainsById.TryGetValue(chainId, out ChainDefinition chain);
            return chain;
        }

        public bool IsValidType(ItemType type)
        {
            ChainDefinition chain = GetChain(type.ChainId);
            return chain != null && chain.IsValidTier(type.Tier);
        }

        public GeneratorDefinition GetGenerator(ItemType type)
        {
            ChainDefinition chain = GetChain(type.ChainId);
            return chain == null ? null : chain.GetGenerator(type.Tier);
        }

        public bool CanMerge(ItemType a, ItemType b)
        {
            if (!a.Equals(b))
            {
                return false;
            }

            ChainDefinition chain = GetChain(a.ChainId);
            return chain != null && chain.IsValidTier(a.Tier) && a.Tier < chain.MaxTier;
        }

        public int GetSellValue(ItemType type)
        {
            ChainDefinition chain = GetChain(type.ChainId);
            return chain == null ? 0 : chain.GetSellValue(type.Tier);
        }

        public long GetMergeValue(ItemType type)
        {
            ChainDefinition chain = GetChain(type.ChainId);
            return chain == null ? 0 : chain.GetMergeValue(type.Tier);
        }
    }
}
