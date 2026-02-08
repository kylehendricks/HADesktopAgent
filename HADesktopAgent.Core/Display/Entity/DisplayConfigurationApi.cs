using System.Text.Json;
using HADesktopAgent.Core.Mqtt;
using Microsoft.Extensions.Logging;

namespace HADesktopAgent.Core.Display.Entity
{
    public class DisplayConfigurationApi : IMqttApi
    {
        private readonly ILogger<DisplayConfigurationApi> _logger;
        private readonly IDisplayWatcher _displayWatcher;
        private readonly IMonitorSwitcher _monitorSwitcher;

        public string Name => "display_config";

        public DisplayConfigurationApi(ILogger<DisplayConfigurationApi> logger, IDisplayWatcher displayWatcher, IMonitorSwitcher monitorSwitcher)
        {
            _logger = logger;
            _displayWatcher = displayWatcher;
            _monitorSwitcher = monitorSwitcher;
        }

        public void HandleCommand(string payload)
        {
            string[]? monitorNames;
            try
            {
                monitorNames = JsonSerializer.Deserialize<string[]>(payload);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to parse display_config payload: {Payload}", payload);
                return;
            }

            if (monitorNames == null || monitorNames.Length == 0)
            {
                _logger.LogWarning("Refusing to apply display configuration — payload would leave zero monitors active");
                return;
            }

            var enabledSet = new HashSet<string>(monitorNames);
            var available = _displayWatcher.AvailableMonitors;

            foreach (var name in enabledSet)
            {
                if (!available.Contains(name))
                {
                    _logger.LogWarning("Monitor '{MonitorName}' is not available, ignoring", name);
                }
            }

            var validEnabled = new HashSet<string>(enabledSet.Where(n => available.Contains(n)));

            if (validEnabled.Count == 0)
            {
                _logger.LogWarning("Refusing to apply display configuration — no valid monitors would be active");
                return;
            }

            _logger.LogInformation("Applying display configuration: enabling [{Monitors}]",
                string.Join(", ", validEnabled));

            _monitorSwitcher.ApplyConfiguration(validEnabled);
        }
    }
}
