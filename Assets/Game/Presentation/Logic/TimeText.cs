namespace Game.Presentation
{
    /// <summary>
    /// "m:ss" countdown strings, cached so per-frame timer displays do not allocate. Values above the cache
    /// range are formatted on demand.
    /// </summary>
    public static class TimeText
    {
        private const int CachedSeconds = 60 * 60; // up to 60:00
        private static readonly string[] Cache = new string[CachedSeconds + 1];

        /// <summary>Formats a remaining time, rounding up to whole seconds (so 0:00 is shown only at zero).</summary>
        public static string Format(long ms)
        {
            if (ms < 0)
            {
                ms = 0;
            }

            long seconds = (ms + 999) / 1000;
            if (seconds <= CachedSeconds)
            {
                int s = (int)seconds;
                return Cache[s] ?? (Cache[s] = Build(seconds));
            }

            return Build(seconds);
        }

        private static string Build(long seconds)
        {
            long minutes = seconds / 60;
            long rest = seconds % 60;
            return minutes + ":" + (rest < 10 ? "0" : "") + rest;
        }
    }
}
