namespace Game.Domain
{
    /// <summary>
    /// One live item instance. Presentation may read it but cannot change it: all mutation goes
    /// through <see cref="MergeEngine"/> commands.
    /// </summary>
    public sealed class Item
    {
        internal Item(ItemId id, ItemType type, bool isGenerator)
        {
            Id = id;
            Type = type;
            IsGenerator = isGenerator;
            Location = Location.Nowhere;
        }

        public ItemId Id { get; }
        public ItemType Type { get; }
        public bool IsGenerator { get; }
        public Location Location { get; internal set; }

        /// <summary>Stored charges (generators only). Use <see cref="MergeEngine.GetAvailableCharges"/> for the time-adjusted value.</summary>
        public int GeneratorCharges { get; internal set; }

        /// <summary>
        /// When the charge currently recharging started (ms). Meaningful only while stored charges are below
        /// max (the timer runs exactly then); 0 when full.
        /// </summary>
        public long GeneratorRechargeStartMs { get; internal set; }

        public override string ToString() => Id + "(" + Type + ")@" + Location;
    }
}
