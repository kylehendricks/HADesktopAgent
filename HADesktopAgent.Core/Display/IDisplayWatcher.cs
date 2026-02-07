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
    }
}
