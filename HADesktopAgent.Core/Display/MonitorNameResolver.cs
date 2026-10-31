namespace HADesktopAgent.Core.Display
{
    /// <summary>
    /// Applies <see cref="NameMappingConfiguration.Monitors"/> to the names the OS reports.
    /// Shared by the per-monitor switches and the profile select so both agree on what a
    /// monitor is called.
    /// </summary>
    public sealed class MonitorNameResolver
    {
        private readonly IDisplayWatcher _displayWatcher;
        private readonly IReadOnlyDictionary<string, string> _mappings;

        public MonitorNameResolver(IDisplayWatcher displayWatcher, IReadOnlyDictionary<string, string>? mappings)
        {
            _displayWatcher = displayWatcher;
            _mappings = mappings ?? new Dictionary<string, string>();
        }

        /// <summary>
        /// Resolves the display name for a monitor. Tries the full EDID identifier
        /// (with serial), then the model-only identifier, then the OS display name, and
        /// falls back to the OS name when nothing is mapped.
        /// </summary>
        public string Resolve(string originalName)
        {
            if (_displayWatcher.MonitorDetails.TryGetValue(originalName, out var monitorInfo)
                && monitorInfo.EdidIdentifier is { } edidId)
            {
                // e.g. "SAM-7796-HNTXA00720"
                if (_mappings.TryGetValue(edidId, out var mappedByFullId))
                    return mappedByFullId;

                // e.g. "SAM-7796" from "SAM-7796-HNTXA00720"
                var firstDash = edidId.IndexOf('-');
                var secondDash = firstDash < 0 ? -1 : edidId.IndexOf('-', firstDash + 1);
                if (secondDash > 0 && _mappings.TryGetValue(edidId[..secondDash], out var mappedByModelId))
                    return mappedByModelId;
            }

            return _mappings.TryGetValue(originalName, out var mappedByName) ? mappedByName : originalName;
        }

        /// <summary>
        /// Maps resolved display name to OS name for every currently available monitor.
        /// </summary>
        public Dictionary<string, string> GetAvailableByDisplayName()
        {
            var result = new Dictionary<string, string>();
            foreach (var original in _displayWatcher.AvailableMonitors)
            {
                result.TryAdd(Resolve(original), original);
            }
            return result;
        }
    }
}
