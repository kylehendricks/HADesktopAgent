namespace HADesktopAgent.Core.Display
{
    /// <summary>
    /// Represents a connected monitor with its display name and EDID-based identifier.
    /// </summary>
    public record MonitorInfo
    {
        /// <summary>
        /// The display name (EDID product name on Linux, friendly device name on Windows).
        /// e.g., "Odyssey G91SD", "LG TV SSCR2"
        /// </summary>
        public required string Name { get; init; }

        /// <summary>
        /// EDID-based identifier combining manufacturer, product code, and serial number.
        /// Format: "MFG-PRODUCT" or "MFG-PRODUCT-SERIAL" (e.g., "SAM-7796-HNTXA00720").
        /// This identifier is cross-platform (identical on Windows and Linux).
        /// Null if EDID data is unavailable.
        /// </summary>
        public string? EdidIdentifier { get; init; }
    }
}
