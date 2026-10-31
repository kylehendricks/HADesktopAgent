namespace HADesktopAgent.Core.Profiles
{
    /// <summary>
    /// One entry under <c>Profiles</c> in config.json, keyed by the profile's name (which is
    /// also the option shown in Home Assistant). Every name refers to a device by its
    /// <see cref="NameMappingConfiguration"/> name, so swapping hardware means changing a
    /// mapping, not the profiles or anything in Home Assistant.
    /// </summary>
    public class ProfileConfiguration
    {
        /// <summary>
        /// Monitors to enable. Every other monitor is disabled. Also what identifies the
        /// profile as active: the profile select reports the profile whose set matches the
        /// active monitors.
        /// </summary>
        public List<string> Monitors { get; set; } = [];

        /// <summary>Audio output to make the default, or null to leave audio alone.</summary>
        public string? Audio { get; set; }

        /// <summary>Refresh rate in Hz per monitor. Keys must also appear in <see cref="Monitors"/>.</summary>
        public Dictionary<string, int> RefreshRates { get; set; } = [];
    }
}
