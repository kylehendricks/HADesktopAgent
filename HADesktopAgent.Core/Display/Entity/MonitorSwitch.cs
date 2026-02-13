using HADesktopAgent.Core.Entity;
using Microsoft.Extensions.Logging;

namespace HADesktopAgent.Core.Display.Entity
{
    public class MonitorSwitch : IHaStatefulEntity, IHaCommandableEntity, IHaClassifiableDevice
    {
        public string Name { get; }
        public string PrettyName { get; }
        public string UniqueId { get; }
        public string Icon => "mdi:monitor";
        public string EntityType => "switch";
        public string DeviceClass => "switch";
        public bool Optimistic => false;

        public string? State => _enabled switch
        {
            true => IHaStatefulEntity.ON_STATE,
            false => IHaStatefulEntity.OFF_STATE,
            null => null
        };

        public event IHaStatefulEntity.StateUpdatedHandler? StateUpdated;
        public event IHaEntity.ConfigUpdatedHandler? ConfigUpdated { add { } remove { } }

        private readonly string _originalMonitorName;
        private readonly IMonitorSwitcher _monitorSwitcher;
        private readonly IDisplayWatcher _displayWatcher;
        private readonly Dictionary<string, string> _mappedToOriginalNames;
        private readonly ILogger _logger;

        private bool? _enabled;

        /// <summary>
        /// Creates a monitor switch entity.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="displayName">The name to display in Home Assistant (may be mapped from config).</param>
        /// <param name="originalMonitorName">The original hardware monitor name used for control operations.</param>
        /// <param name="monitorSwitcher">The monitor switcher implementation.</param>
        /// <param name="displayWatcher">The display watcher implementation.</param>
        /// <param name="mappedToOriginalNames">Mapping from display names to original hardware names.</param>
        public MonitorSwitch(
            ILogger logger,
            string displayName,
            string originalMonitorName,
            IMonitorSwitcher monitorSwitcher,
            IDisplayWatcher displayWatcher,
            Dictionary<string, string> mappedToOriginalNames)
        {
            _logger = logger;
            _originalMonitorName = originalMonitorName;
            _monitorSwitcher = monitorSwitcher;
            _displayWatcher = displayWatcher;
            _mappedToOriginalNames = mappedToOriginalNames;

            Name = "display_" + SanitizeName(displayName);
            PrettyName = displayName;
            UniqueId = Name;

            _enabled = displayWatcher.ActiveMonitors.Contains(originalMonitorName);
        }

        public void HandleCommand(string command)
        {
            switch (command)
            {
                case IHaStatefulEntity.ON_STATE:
                    _monitorSwitcher.SetMonitorEnabled(_originalMonitorName, true);
                    return;
                case IHaStatefulEntity.OFF_STATE:
                    if (_displayWatcher.ActiveMonitors.Count <= 1 && _enabled == true)
                    {
                        _logger.LogWarning("Refusing to disable {Monitor} — it is the last active monitor", _originalMonitorName);
                        return;
                    }
                    _monitorSwitcher.SetMonitorEnabled(_originalMonitorName, false);
                    return;
                default:
                    _logger.LogWarning("Invalid command for {Entity}: {Command}", Name, command);
                    break;
            }
        }

        public void UpdateState(bool enabled)
        {
            if (_enabled == enabled)
                return;

            _enabled = enabled;
            StateUpdated?.Invoke(this);
        }

        private static string SanitizeName(string name)
        {
            return name.ToLowerInvariant()
                .Replace(' ', '_')
                .Replace('-', '_');
        }
    }
}
