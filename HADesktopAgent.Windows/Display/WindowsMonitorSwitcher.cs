using HADesktopAgent.Core.Display;

namespace HADesktopAgent.Windows.Display
{
    public class WindowsMonitorSwitcher : IMonitorSwitcher
    {
        public bool SetMonitorEnabled(string monitorName, bool enabled)
        {
            if (enabled)
            {
                var monitor = MonitorSwitcher.GetMonitors().Find(m => m.Name == monitorName);
                if (monitor == null)
                    return false;

                return MonitorSwitcher.SwitchToMonitor(monitor);
            }

            // Disabling individual monitors is not yet supported on Windows
            System.Diagnostics.Debug.WriteLine($"Disabling monitor '{monitorName}' is not supported on Windows");
            return false;
        }
    }
}
