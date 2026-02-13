namespace HADesktopAgent.Core
{
    /// <summary>
    /// Configuration for mapping device identifiers to custom display names.
    /// Monitor keys can be EDID identifiers (e.g., "SAM-7796-HNTXA00720") or display names (e.g., "Odyssey G91SD").
    /// Audio device keys are device names (e.g., "Realtek High Definition Audio").
    /// Values are the custom names to display in Home Assistant.
    /// </summary>
    public class NameMappingConfiguration
    {
        /// <summary>
        /// Monitor name mappings. Keys can be:
        /// - EDID identifier with serial: "MFG-PRODUCT-SERIAL" (e.g., "SAM-7796-HNTXA00720") for unique per-instance mapping
        /// - EDID identifier without serial: "MFG-PRODUCT" (e.g., "SAM-7796") for model-level mapping
        /// - Display product name: (e.g., "Odyssey G91SD") for simple name-based mapping
        /// EDID identifiers are cross-platform (identical on Windows and Linux).
        /// </summary>
        public Dictionary<string, string> Monitors { get; set; } = new();

        /// <summary>
        /// Audio device name mappings. Keys are device names as reported by the OS.
        /// On Windows: the interface friendly name (e.g., "Realtek High Definition Audio").
        /// On Linux: the PulseAudio sink description (e.g., "Built-in Audio Digital Stereo (HDMI)").
        /// </summary>
        public Dictionary<string, string> AudioDevices { get; set; } = new();
    }
}
