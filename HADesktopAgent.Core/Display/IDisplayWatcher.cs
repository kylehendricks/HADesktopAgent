namespace HADesktopAgent.Core.Display
{
    public interface IDisplayWatcher
    {
        delegate void AvailableMonitorsUpdatedHandler();
        delegate void ActiveMonitorsUpdatedHandler();

        event AvailableMonitorsUpdatedHandler? AvailableMonitorsUpdated;
        event ActiveMonitorsUpdatedHandler? ActiveMonitorsUpdated;

        SortedSet<string> AvailableMonitors { get; }
        SortedSet<string> ActiveMonitors { get; }

        /// <summary>
        /// Provides detailed information about available monitors, keyed by display name.
        /// Includes EDID-based identifiers for name mapping support.
        /// </summary>
        Dictionary<string, MonitorInfo> MonitorDetails { get; }
    }
}
