using DesktopManager;
using HADesktopAgent.Core.Display;

namespace HADesktopAgent.Windows.Display
{
    public sealed class DesktopManagerDisplayWatcher : IDisplayWatcher, IDisposable
    {
        public event IDisplayWatcher.AvailableMonitorsUpdatedHandler? AvailableMonitorsUpdated;
        public event IDisplayWatcher.ActiveMonitorsUpdatedHandler? ActiveMonitorsUpdated;

        private readonly MonitorWatcher _monitorWatcher;
        private SortedSet<string> _availableMonitors = [];
        private SortedSet<string> _activeMonitors = [];
        private Dictionary<string, MonitorInfo> _monitorDetails = new();
        private System.Threading.Timer? _delayedUpdateTimer;
        private readonly Lock _timerLock = new();

        public DesktopManagerDisplayWatcher(MonitorWatcher monitorWatcher)
        {
            _monitorWatcher = monitorWatcher;

            UpdateState();

            _monitorWatcher.DisplaySettingsChanged += DisplayStatusChanged;
            _monitorWatcher.OrientationChanged += DisplayStatusChanged;
            _monitorWatcher.ResolutionChanged += DisplayStatusChanged;
        }
        public SortedSet<string> AvailableMonitors => _availableMonitors;
        public SortedSet<string> ActiveMonitors => _activeMonitors;
        public Dictionary<string, MonitorInfo> MonitorDetails => _monitorDetails;

        public void Dispose()
        {
            _monitorWatcher.ResolutionChanged -= DisplayStatusChanged;
            _monitorWatcher.OrientationChanged -= DisplayStatusChanged;
            _monitorWatcher.DisplaySettingsChanged -= DisplayStatusChanged;

            lock (_timerLock)
            {
                _delayedUpdateTimer?.Dispose();
                _delayedUpdateTimer = null;
            }
        }
        private void DisplayStatusChanged(object? sender, EventArgs e)
        {
            UpdateState();

            lock (_timerLock)
            {
                _delayedUpdateTimer?.Dispose();

                _delayedUpdateTimer = new System.Threading.Timer(
                    callback: _ => UpdateState(),
                    state: null,
                    dueTime: TimeSpan.FromSeconds(1),
                    period: Timeout.InfiniteTimeSpan
                );
            }
        }

        private void UpdateState()
        {
            var monitors = MonitorSwitcher.GetMonitors();
            var availableMonitors = monitors.Select(m => m.Name);
            var activeMonitors = monitors.FindAll(m => m.IsActive).Select(m => m.Name);
            var monitorDetails = new Dictionary<string, MonitorInfo>();

            foreach (var monitor in monitors)
            {
                monitorDetails.TryAdd(monitor.Name, new MonitorInfo
                {
                    Name = monitor.Name,
                    EdidIdentifier = monitor.EdidIdentifier
                });
            }

            _monitorDetails = monitorDetails;

            if (!_availableMonitors.SetEquals(availableMonitors))
            {
                _availableMonitors = [.. availableMonitors];
                AvailableMonitorsUpdated?.Invoke();
            }

            if (!_activeMonitors.SetEquals(activeMonitors))
            {
                _activeMonitors = [.. activeMonitors];
                ActiveMonitorsUpdated?.Invoke();
            }
        }
    }
}
