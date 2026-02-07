using HADesktopAgent.Core.Display.Entity;
using HADesktopAgent.Core.Mqtt;
using Microsoft.Extensions.Logging;

namespace HADesktopAgent.Core.Display
{
    public class MonitorSwitchManager : IDisposable
    {
        private readonly ILogger<MonitorSwitchManager> _logger;
        private readonly IDisplayWatcher _displayWatcher;
        private readonly IMonitorSwitcher _monitorSwitcher;
        private readonly MqttHaManager _mqttHaManager;
        private readonly ILoggerFactory _loggerFactory;
        private readonly Dictionary<string, MonitorSwitch> _switches = new();

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

            // Add new monitors
            foreach (var monitor in current)
            {
                if (!existing.Contains(monitor))
                    CreateSwitch(monitor);
            }

            // Remove gone monitors
            foreach (var monitor in existing)
            {
                if (!current.Contains(monitor))
                    RemoveSwitch(monitor);
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
        }
    }
}
