using System.Diagnostics;
using HADesktopAgent.Core.Display;
using Microsoft.Extensions.Logging;

namespace HADesktopAgent.Linux.Display
{
    public class KScreenMonitorSwitcher : IMonitorSwitcher
    {
        private readonly ILogger<KScreenMonitorSwitcher> _logger;

        public KScreenMonitorSwitcher(ILogger<KScreenMonitorSwitcher> logger)
        {
            _logger = logger;
        }

        public bool SetMonitorEnabled(string monitorName, bool enabled)
        {
            try
            {
                var edidMap = DrmEdidHelper.GetConnectorToFriendlyNameMap(_logger);

                // Find the target connector for the friendly name
                var targetConnector = edidMap.FirstOrDefault(kvp => kvp.Value == monitorName).Key;
                if (targetConnector == null)
                {
                    // Maybe monitorName is already a connector name
                    if (edidMap.ContainsKey(monitorName))
                        targetConnector = monitorName;
                    else
                    {
                        _logger.LogWarning("Monitor '{MonitorName}' not found in EDID map", monitorName);
                        return false;
                    }
                }

                var action = enabled ? "enable" : "disable";
                var arguments = $"output.{targetConnector}.{action}";

                _logger.LogInformation("{Action} monitor {MonitorName} ({Connector}): kscreen-doctor {Args}",
                    enabled ? "Enabling" : "Disabling", monitorName, targetConnector, arguments);

                var startInfo = new ProcessStartInfo
                {
                    FileName = "kscreen-doctor",
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    _logger.LogError("Failed to start kscreen-doctor process");
                    return false;
                }

                process.WaitForExit(TimeSpan.FromSeconds(10));

                if (process.ExitCode != 0)
                {
                    var stderr = process.StandardError.ReadToEnd();
                    _logger.LogError("kscreen-doctor failed with exit code {ExitCode}: {Stderr}",
                        process.ExitCode, stderr);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to {Action} monitor {MonitorName}",
                    enabled ? "enable" : "disable", monitorName);
                return false;
            }
        }
    }
}
