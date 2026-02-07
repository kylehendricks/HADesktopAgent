using HADesktopAgent.Core.Display;

namespace HADesktopAgent.Windows.Display
{
    public class WindowsMonitorSwitcher : IMonitorSwitcher
    {
        public bool SwitchToMonitor(string monitorName)
        {
            var monitor = MonitorSwitcher.GetMonitors().Find(m => m.Name == monitorName);

            if (monitor == null)
            {
                return false;
            }

            return MonitorSwitcher.SwitchToMonitor(monitor);
        }
    }
}
