namespace HADesktopAgent.Core.Display
{
    public interface IDisplayWatcher
    {
        delegate void AvailableMonitorsUpdatedHandler();
        delegate void ActiveMonitorsUpdatedHandler();
        delegate void DisplaySettingsUpdatedHandler();

        event AvailableMonitorsUpdatedHandler? AvailableMonitorsUpdated;
        event ActiveMonitorsUpdatedHandler? ActiveMonitorsUpdated;

        /// <summary>
        /// Fires whenever display settings may have changed (mode, refresh rate,
        /// topology), including changes that don't alter the available/active monitor
        /// sets. May fire spuriously; subscribers should re-query and diff.
        /// </summary>
        event DisplaySettingsUpdatedHandler? DisplaySettingsUpdated;

        SortedSet<string> AvailableMonitors { get; }
        SortedSet<string> ActiveMonitors { get; }

        /// <summary>
        /// Provides detailed information about available monitors, keyed by display name.
        /// Includes EDID-based identifiers for name mapping support.
        /// </summary>
        Dictionary<string, MonitorInfo> MonitorDetails { get; }
    }
}
