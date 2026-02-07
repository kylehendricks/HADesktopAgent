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

        private readonly string _monitorName;
        private readonly IMonitorSwitcher _monitorSwitcher;
        private readonly IDisplayWatcher _displayWatcher;
        private readonly ILogger _logger;

        private bool? _enabled;

        public MonitorSwitch(ILogger logger, string monitorName, IMonitorSwitcher monitorSwitcher, IDisplayWatcher displayWatcher)
        {
            _logger = logger;
            _monitorName = monitorName;
            _monitorSwitcher = monitorSwitcher;
            _displayWatcher = displayWatcher;

            Name = "display_" + SanitizeName(monitorName);
            PrettyName = monitorName;
            UniqueId = Name;

            _enabled = displayWatcher.ActiveMonitors.Contains(monitorName);
        }

        public void HandleCommand(string command)
        {
            switch (command)
            {
                case IHaStatefulEntity.ON_STATE:
                    _monitorSwitcher.SetMonitorEnabled(_monitorName, true);
                    return;
                case IHaStatefulEntity.OFF_STATE:
                    if (_displayWatcher.ActiveMonitors.Count <= 1 && _enabled == true)
                    {
                        _logger.LogWarning("Refusing to disable {Monitor} — it is the last active monitor", _monitorName);
                        return;
                    }
                    _monitorSwitcher.SetMonitorEnabled(_monitorName, false);
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
