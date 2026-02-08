using HADesktopAgent.Core.Display;

namespace HADesktopAgent.Windows.Display
{
    public class WindowsMonitorSwitcher : IMonitorSwitcher
    {
        public bool ApplyConfiguration(ISet<string> enabledMonitors)
        {
            return MonitorSwitcher.ApplyConfiguration(enabledMonitors);
        }

        public bool SetMonitorEnabled(string monitorName, bool enabled)
        {
            var activeNames = new HashSet<string>(
                MonitorSwitcher.GetMonitors().Where(m => m.IsActive).Select(m => m.Name));

            if (enabled)
                activeNames.Add(monitorName);
            else
                activeNames.Remove(monitorName);

            return MonitorSwitcher.ApplyConfiguration(activeNames);
        }
    }
}
