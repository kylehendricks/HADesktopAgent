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
        private readonly Dictionary<string, string> _monitorNameMappings;
        private readonly Dictionary<string, MonitorSwitch> _switches = new();
        private readonly Dictionary<string, Timer> _pendingRemovals = new();

        /// <summary>
        /// Maps mapped display names back to original hardware names for control operations.
        /// Key: mapped name (or original if no mapping), Value: original hardware name.
        /// Exposed as a property so DisplayConfigurationApi can share the same live mapping.
        /// </summary>
        private readonly Dictionary<string, string> _mappedToOriginalNames = new();

        /// <summary>
        /// Gets the live mapping from display names (possibly mapped) to original hardware names.
        /// This dictionary is kept in sync as monitors are added/removed.
        /// </summary>
        public IReadOnlyDictionary<string, string> MappedToOriginalNames => _mappedToOriginalNames;

        public MonitorSwitchManager(
            ILogger<MonitorSwitchManager> logger,
            ILoggerFactory loggerFactory,
            IDisplayWatcher displayWatcher,
            IMonitorSwitcher monitorSwitcher,
            MqttHaManager mqttHaManager,
            Dictionary<string, string>? monitorNameMappings = null)
        {
            _logger = logger;
            _loggerFactory = loggerFactory;
            _displayWatcher = displayWatcher;
            _monitorSwitcher = monitorSwitcher;
            _mqttHaManager = mqttHaManager;
            _monitorNameMappings = monitorNameMappings ?? new();

            // Create switches for currently available monitors
            foreach (var monitor in _displayWatcher.AvailableMonitors)
            {
                var mappedName = ResolveMappedName(monitor);
                CreateSwitch(monitor, mappedName);
            }

            _displayWatcher.AvailableMonitorsUpdated += HandleAvailableMonitorsUpdated;
            _displayWatcher.ActiveMonitorsUpdated += HandleActiveMonitorsUpdated;
        }

        /// <summary>
        /// Resolves the display name for a monitor by checking name mappings.
        /// Tries matching by EDID identifier first (most specific with serial, then model-only),
        /// then by original display name, falls back to the original name if no mapping exists.
        /// </summary>
        private string ResolveMappedName(string originalName)
        {
            var details = _displayWatcher.MonitorDetails;

            if (details.TryGetValue(originalName, out var monitorInfo) && monitorInfo.EdidIdentifier != null)
            {
                // Try exact EDID identifier match (e.g., "SAM-7796-HNTXA00720")
                if (_monitorNameMappings.TryGetValue(monitorInfo.EdidIdentifier, out var mappedByFullId))
                {
                    _logger.LogInformation(
                        "Monitor '{OriginalName}' mapped to '{MappedName}' via EDID identifier '{EdidId}'",
                        originalName, mappedByFullId, monitorInfo.EdidIdentifier);
                    return mappedByFullId;
                }

                // Try model-only EDID identifier match (e.g., "SAM-7796" from "SAM-7796-HNTXA00720")
                var dashCount = monitorInfo.EdidIdentifier.Count(c => c == '-');
                if (dashCount >= 2)
                {
                    var secondDash = monitorInfo.EdidIdentifier.IndexOf('-', monitorInfo.EdidIdentifier.IndexOf('-') + 1);
                    var modelOnlyId = monitorInfo.EdidIdentifier[..secondDash];
                    if (_monitorNameMappings.TryGetValue(modelOnlyId, out var mappedByModelId))
                    {
                        _logger.LogInformation(
                            "Monitor '{OriginalName}' mapped to '{MappedName}' via model EDID identifier '{EdidId}'",
                            originalName, mappedByModelId, modelOnlyId);
                        return mappedByModelId;
                    }
                }
            }

            // Try matching by display name
            if (_monitorNameMappings.TryGetValue(originalName, out var mappedByName))
            {
                _logger.LogInformation(
                    "Monitor '{OriginalName}' mapped to '{MappedName}' via display name",
                    originalName, mappedByName);
                return mappedByName;
            }

            return originalName;
        }

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
            _ = _mqttHaManager.RegisterEntity(monitorSwitch);
            _logger.LogInformation("Registered monitor switch for '{Monitor}' (original: '{OriginalName}')", displayName, originalMonitorName);
        }

        private void RemoveSwitch(string displayName)
        {
            if (!_switches.Remove(displayName, out var monitorSwitch))
                return;

            _mappedToOriginalNames.Remove(displayName);
            _ = _mqttHaManager.UnregisterEntity(monitorSwitch);
            _logger.LogInformation("Unregistered monitor switch for '{Monitor}'", displayName);
        }

        private void HandleAvailableMonitorsUpdated()
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
                            if (_pendingRemovals.Remove(capturedName))
                            {
                                _logger.LogInformation(
                                    "Monitor '{Monitor}' still absent after delay, removing switch", capturedName);
                                RemoveSwitch(capturedName);
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

            foreach (var (displayName, monitorSwitch) in _switches)
            {
                // Check if the original name for this switch is in the active set
                var originalName = _mappedToOriginalNames.GetValueOrDefault(displayName, displayName);
                monitorSwitch.UpdateState(active.Contains(originalName));
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
