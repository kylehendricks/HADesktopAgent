using HADesktopAgent.Core.Display.Entity;
using HADesktopAgent.Core.Mqtt;
using Microsoft.Extensions.Logging;

namespace HADesktopAgent.Core.Display
{
    public class MonitorSwitchManager : IDisposable
    {
        private static readonly TimeSpan DisconnectDelay = TimeSpan.FromSeconds(15);

        private readonly ILogger<MonitorSwitchManager> _logger;
        private readonly IDisplayWatcher _displayWatcher;
        private readonly IMonitorSwitcher _monitorSwitcher;
        private readonly MqttHaManager _mqttHaManager;
        private readonly ILoggerFactory _loggerFactory;
        private readonly Dictionary<string, MonitorSwitch> _switches = new();
        private readonly Dictionary<string, Timer> _pendingRemovals = new();

        public MonitorSwitchManager(
            ILogger<MonitorSwitchManager> logger,
            ILoggerFactory loggerFactory,
            IDisplayWatcher displayWatcher,
            IMonitorSwitcher monitorSwitcher,
            MqttHaManager mqttHaManager)
        {
            _logger = logger;
            _loggerFactory = loggerFactory;
            _displayWatcher = displayWatcher;
            _monitorSwitcher = monitorSwitcher;
            _mqttHaManager = mqttHaManager;

            // Create switches for currently available monitors
            foreach (var monitor in _displayWatcher.AvailableMonitors)
            {
                CreateSwitch(monitor);
            }

            _displayWatcher.AvailableMonitorsUpdated += HandleAvailableMonitorsUpdated;
            _displayWatcher.ActiveMonitorsUpdated += HandleActiveMonitorsUpdated;
        }

        private void CreateSwitch(string monitorName)
        {
            if (_switches.ContainsKey(monitorName))
                return;

            var monitorSwitch = new MonitorSwitch(
                _loggerFactory.CreateLogger<MonitorSwitch>(),
                monitorName,
                _monitorSwitcher,
                _displayWatcher);

            _switches[monitorName] = monitorSwitch;
            _ = _mqttHaManager.RegisterEntity(monitorSwitch);
            _logger.LogInformation("Registered monitor switch for '{Monitor}'", monitorName);
        }

        private void RemoveSwitch(string monitorName)
        {
            if (!_switches.Remove(monitorName, out var monitorSwitch))
                return;

            _ = _mqttHaManager.UnregisterEntity(monitorSwitch);
            _logger.LogInformation("Unregistered monitor switch for '{Monitor}'", monitorName);
        }

        private void HandleAvailableMonitorsUpdated()
        {
            var current = _displayWatcher.AvailableMonitors;
            var existing = new HashSet<string>(_switches.Keys);

            // Add new monitors (immediate) and cancel any pending removal
            foreach (var monitor in current)
            {
                if (_pendingRemovals.Remove(monitor, out var timer))
                {
                    timer.Dispose();
                    _logger.LogInformation("Monitor '{Monitor}' reappeared, cancelling pending removal", monitor);
                }

                if (!existing.Contains(monitor))
                    CreateSwitch(monitor);
            }

            // Schedule delayed removal for gone monitors
            foreach (var monitor in existing)
            {
                if (!current.Contains(monitor) && !_pendingRemovals.ContainsKey(monitor))
                {
                    _logger.LogInformation(
                        "Monitor '{Monitor}' disappeared, scheduling removal in {Delay}s",
                        monitor, DisconnectDelay.TotalSeconds);

                    var monitorName = monitor; // capture for closure
                    _pendingRemovals[monitorName] = new Timer(
                        _ =>
                        {
                            if (_pendingRemovals.Remove(monitorName))
                            {
                                _logger.LogInformation(
                                    "Monitor '{Monitor}' still absent after delay, removing switch", monitorName);
                                RemoveSwitch(monitorName);
                            }
                        },
                        state: null,
                        dueTime: DisconnectDelay,
                        period: Timeout.InfiniteTimeSpan);
                }
            }
        }

        private void HandleActiveMonitorsUpdated()
        {
            var active = _displayWatcher.ActiveMonitors;

            foreach (var (monitorName, monitorSwitch) in _switches)
            {
                monitorSwitch.UpdateState(active.Contains(monitorName));
            }
        }

        public void Dispose()
        {
            _displayWatcher.AvailableMonitorsUpdated -= HandleAvailableMonitorsUpdated;
            _displayWatcher.ActiveMonitorsUpdated -= HandleActiveMonitorsUpdated;

            foreach (var timer in _pendingRemovals.Values)
                timer.Dispose();
            _pendingRemovals.Clear();
        }
    }
}
