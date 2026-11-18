using System;

namespace Game.Data
{
    /// <summary>
    /// Serialized shape of the merge content file (Assets/Game/Content/Resources/MergeContent.json).
    /// Plain public fields so Unity's JsonUtility (and other serializers) can fill them. Field names are the
    /// JSON keys. Mapped and validated into domain config by <see cref="MergeContentMapper"/>.
    /// </summary>
    [Serializable]
    public sealed class MergeContentData
    {
        public const int SupportedVersion = 1;

        /// <summary>Content format version. 0 (missing) is rejected, never assumed.</summary>
        public int version;
        public BoardData board;
        public ChainData[] chains;
        public LayoutEntryData[] startingLayout;
    }

    [Serializable]
    public sealed class BoardData
    {
        public int columns;
        public int rows;
        public int inventoryCapacity;
        public int maxInventoryCapacity;
        public int[] lockedCells;
    }

    [Serializable]
    public sealed class ChainData
    {
        public string id;
        /// <summary>Placeholder colour for the chain, "#RRGGBB". Presentation only.</summary>
        public string color;
        public int maxTier;
        public bool strictlyDoubling;
        /// <summary>Coin value per tier (index 0 = tier 1).</summary>
        public int[] sellValues;
        /// <summary>Display name per tier (index 0 = tier 1). Optional; placeholder names are generated if missing.</summary>
        public string[] tierNames;
        /// <summary>Generator behaviour for the tiers that are generators. Tiers not listed are ordinary items.</summary>
        public GeneratorTierData[] generators;
    }

    [Serializable]
    public sealed class GeneratorTierData
    {
        public int tier;
        public int maxCharges;
        public long rechargeIntervalMs;
        public OutputData[] outputs;
    }

    [Serializable]
    public sealed class OutputData
    {
        public string chainId;
        public int tier;
        public int weight;
    }

    [Serializable]
    public sealed class LayoutEntryData
    {
        public string chainId;
        public int tier;
        public int cell;
    }
}
