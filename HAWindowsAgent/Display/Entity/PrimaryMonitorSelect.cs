using HAWindowsAgent.Entity;

namespace HAWindowsAgent.Display.Entity
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

        private IDisplayWatcher _displayWatcher;

        public event IHaStatefulEntity.StateUpdatedHandler? StateUpdated;
        public event IHaEntity.ConfigUpdatedHandler? ConfigUpdated;

        public PrimaryMonitorSelect(IDisplayWatcher displayWatcher)
        {
            _displayWatcher = displayWatcher;

            _displayWatcher.ActiveMonitorsUpdated += ActiveMonitorsChanged;
            _displayWatcher.AvailableMonitorsUpdated += AvailableMonitorsChanged;
        }

        public void HandleCommand(string command)
        {
            var monitor = MonitorSwitcher.GetMonitors().Find(m => m.Name == command);

            if (monitor == null)
            {
                Console.WriteLine($"Monitor '{command}' not found");
                return;
            }

            MonitorSwitcher.SwitchToMonitor(monitor);
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
