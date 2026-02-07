using HADesktopAgent.Core.Entity;

namespace HADesktopAgent.Core.Display.Entity
{
    public class PrimaryMonitorSelect : IHaStatefulEntity, IHaCommandableEntity, IHaSelectableEntity, IDisposable
    {
        public string Name => "display";
        public string PrettyName => "Display";
        public string UniqueId => "display";
        public string Icon => "mdi:monitor";
        public bool Optimistic => false;
        public string EntityType => "select";
        public SortedSet<string> Options => _displayWatcher.AvailableMonitors;
        public string? State => _displayWatcher.ActiveMonitors.FirstOrDefault();

        private readonly IDisplayWatcher _displayWatcher;
        private readonly IMonitorSwitcher _monitorSwitcher;

        public event IHaStatefulEntity.StateUpdatedHandler? StateUpdated;
        public event IHaEntity.ConfigUpdatedHandler? ConfigUpdated;

        public PrimaryMonitorSelect(IDisplayWatcher displayWatcher, IMonitorSwitcher monitorSwitcher)
        {
            _displayWatcher = displayWatcher;
            _monitorSwitcher = monitorSwitcher;

            _displayWatcher.ActiveMonitorsUpdated += ActiveMonitorsChanged;
            _displayWatcher.AvailableMonitorsUpdated += AvailableMonitorsChanged;
        }

        public void HandleCommand(string command)
        {
            _monitorSwitcher.SwitchToMonitor(command);
        }

        private void AvailableMonitorsChanged()
        {
            ConfigUpdated?.Invoke(this);
        }

        private void ActiveMonitorsChanged()
        {
            StateUpdated?.Invoke(this);
        }

        public void Dispose()
        {
            _displayWatcher.AvailableMonitorsUpdated -= AvailableMonitorsChanged;
            _displayWatcher.ActiveMonitorsUpdated -= ActiveMonitorsChanged;
        }
    }
}
