namespace HADesktopAgent.Core.Display
{
    public interface IMonitorSwitcher
    {
        bool SetMonitorEnabled(string monitorName, bool enabled);
        bool ApplyConfiguration(ISet<string> enabledMonitors);
    }
}
