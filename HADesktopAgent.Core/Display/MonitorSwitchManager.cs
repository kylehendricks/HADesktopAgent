using HADesktopAgent.Core.Display.Entity;
using HADesktopAgent.Core.Entity;
using Microsoft.Extensions.Logging;

namespace HADesktopAgent.Core.Display
{
    public class MonitorSwitchManager : IDisposable
    {
        private static readonly TimeSpan DisconnectDelay = TimeSpan.FromSeconds(15);

        private readonly ILogger<MonitorSwitchManager> _logger;
        private readonly IDisplayWatcher _displayWatcher;
        private readonly IMonitorSwitcher _monitorSwitcher;
        private readonly IHaEntityHost _entityHost;
        private readonly ILoggerFactory _loggerFactory;
        private readonly MonitorNameResolver _nameResolver;
        private readonly IRefreshRateController? _refreshRateController;
        private readonly Dictionary<string, MonitorSwitch> _switches = new();
        private readonly Dictionary<string, RefreshRateSelect> _refreshRateSelects = new();
        private readonly Dictionary<string, Timer> _pendingRemovals = new();

        // Guards _switches/_pendingRemovals/_mappedToOriginalNames, which are hit from
        // both display-watcher event threads and removal Timer callbacks.
        private readonly object _lock = new();

        /// <summary>
        /// Maps mapped display names back to original hardware names for control operations.
        /// Key: mapped name (or original if no mapping), Value: original hardware name.
        /// </summary>
        private readonly Dictionary<string, string> _mappedToOriginalNames = new();

        public MonitorSwitchManager(
            ILogger<MonitorSwitchManager> logger,
            ILoggerFactory loggerFactory,
            IDisplayWatcher displayWatcher,
            IMonitorSwitcher monitorSwitcher,
            IHaEntityHost entityHost,
            MonitorNameResolver nameResolver,
            IRefreshRateController? refreshRateController = null)
        {
            _logger = logger;
            _loggerFactory = loggerFactory;
            _displayWatcher = displayWatcher;
            _monitorSwitcher = monitorSwitcher;
            _entityHost = entityHost;
            _nameResolver = nameResolver;
            _refreshRateController = refreshRateController;

            // Create switches for currently available monitors
            foreach (var monitor in _displayWatcher.AvailableMonitors)
            {
                var mappedName = ResolveMappedName(monitor);
                CreateSwitch(monitor, mappedName);
            }

            _displayWatcher.AvailableMonitorsUpdated += HandleAvailableMonitorsUpdated;
            _displayWatcher.ActiveMonitorsUpdated += HandleActiveMonitorsUpdated;
        }

        private string ResolveMappedName(string originalName) => _nameResolver.Resolve(originalName);

        private void CreateSwitch(string originalMonitorName, string displayName)
        {
            if (_switches.ContainsKey(displayName))
                return;

            _mappedToOriginalNames[displayName] = originalMonitorName;

            var monitorSwitch = new MonitorSwitch(
                _loggerFactory.CreateLogger<MonitorSwitch>(),
                displayName,
                originalMonitorName,
                _monitorSwitcher,
                _displayWatcher,
                _mappedToOriginalNames);

            _switches[displayName] = monitorSwitch;
            _ = _entityHost.RegisterEntity(monitorSwitch);
            _logger.LogInformation("Registered monitor switch for '{Monitor}' (original: '{OriginalName}')", displayName, originalMonitorName);

            if (_refreshRateController != null)
            {
                var refreshRateSelect = new RefreshRateSelect(
                    _loggerFactory.CreateLogger<RefreshRateSelect>(),
                    displayName,
                    originalMonitorName,
                    _refreshRateController,
                    _displayWatcher);

                _refreshRateSelects[displayName] = refreshRateSelect;
                _ = _entityHost.RegisterEntity(refreshRateSelect);
                _logger.LogInformation("Registered refresh rate select for '{Monitor}'", displayName);
            }
        }

        private void RemoveSwitch(string displayName)
        {
            if (!_switches.Remove(displayName, out var monitorSwitch))
                return;

            _mappedToOriginalNames.Remove(displayName);
            _ = _entityHost.UnregisterEntity(monitorSwitch);
            _logger.LogInformation("Unregistered monitor switch for '{Monitor}'", displayName);

            if (_refreshRateSelects.Remove(displayName, out var refreshRateSelect))
            {
                refreshRateSelect.Dispose();
                _ = _entityHost.UnregisterEntity(refreshRateSelect);
            }
        }

        private void HandleAvailableMonitorsUpdated()
        {
            lock (_lock)
            {
                var current = _displayWatcher.AvailableMonitors;
                var existing = new HashSet<string>(_switches.Keys);

                // Build set of current mapped names
                var currentMappedNames = new HashSet<string>();
                foreach (var monitor in current)
                {
                    var mappedName = ResolveMappedName(monitor);
                    currentMappedNames.Add(mappedName);

                    if (_pendingRemovals.Remove(mappedName, out var timer))
                    {
                        timer.Dispose();
                        _logger.LogInformation("Monitor '{Monitor}' reappeared, cancelling pending removal", mappedName);
                    }

                    if (!existing.Contains(mappedName))
                        CreateSwitch(monitor, mappedName);
                }

                // Schedule delayed removal for gone monitors
                foreach (var displayName in existing)
                {
                    if (!currentMappedNames.Contains(displayName) && !_pendingRemovals.ContainsKey(displayName))
                    {
                        _logger.LogInformation(
                            "Monitor '{Monitor}' disappeared, scheduling removal in {Delay}s",
                            displayName, DisconnectDelay.TotalSeconds);

                        var capturedName = displayName; // capture for closure
                        _pendingRemovals[capturedName] = new Timer(
                            _ =>
                            {
                                lock (_lock)
                                {
                                    if (_pendingRemovals.Remove(capturedName))
                                    {
                                        _logger.LogInformation(
                                            "Monitor '{Monitor}' still absent after delay, removing switch", capturedName);
                                        RemoveSwitch(capturedName);
                                    }
                                }
                            },
                            state: null,
                            dueTime: DisconnectDelay,
                            period: Timeout.InfiniteTimeSpan);
                    }
                }
            }
        }

        private void HandleActiveMonitorsUpdated()
        {
            lock (_lock)
            {
                var active = _displayWatcher.ActiveMonitors;

                foreach (var (displayName, monitorSwitch) in _switches)
                {
                    // Check if the original name for this switch is in the active set
                    var originalName = _mappedToOriginalNames.GetValueOrDefault(displayName, displayName);
                    monitorSwitch.UpdateState(active.Contains(originalName));
                }
            }
        }

        public void Dispose()
        {
            _displayWatcher.AvailableMonitorsUpdated -= HandleAvailableMonitorsUpdated;
            _displayWatcher.ActiveMonitorsUpdated -= HandleActiveMonitorsUpdated;

            lock (_lock)
            {
                foreach (var timer in _pendingRemovals.Values)
                    timer.Dispose();
                _pendingRemovals.Clear();

                foreach (var refreshRateSelect in _refreshRateSelects.Values)
                    refreshRateSelect.Dispose();
                _refreshRateSelects.Clear();
            }
        }
    }
}
