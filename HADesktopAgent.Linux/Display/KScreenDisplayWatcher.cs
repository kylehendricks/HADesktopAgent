using System.Diagnostics;
using System.Text.RegularExpressions;
using HADesktopAgent.Core.Display;
using Microsoft.Extensions.Logging;

namespace HADesktopAgent.Linux.Display
{
    public sealed partial class KScreenDisplayWatcher : IDisplayWatcher, IDisposable
    {
        public event IDisplayWatcher.AvailableMonitorsUpdatedHandler? AvailableMonitorsUpdated;
        public event IDisplayWatcher.ActiveMonitorsUpdatedHandler? ActiveMonitorsUpdated;

        public SortedSet<string> AvailableMonitors { get; private set; } = [];
        public SortedSet<string> ActiveMonitors { get; private set; } = [];
        public Dictionary<string, MonitorInfo> MonitorDetails { get; private set; } = new();

        private readonly ILogger<KScreenDisplayWatcher> _logger;
        private readonly Timer _pollTimer;

        // Matches "Output: <id> <connector> <uuid>"
        [GeneratedRegex(@"^Output:\s+\d+\s+(\S+)\s+\S+")]
        private static partial Regex OutputHeaderRegex();

        public KScreenDisplayWatcher(ILogger<KScreenDisplayWatcher> logger)
        {
            _logger = logger;

            // Initial poll
            PollDisplays();

            // Poll every 5 seconds
            _pollTimer = new Timer(_ => PollDisplays(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        }

        private void PollDisplays()
        {
            try
            {
                var output = RunKScreenDoctor();
                if (output == null) return;

                var edidMap = DrmEdidHelper.GetConnectorToMonitorInfoMap(_logger);
                var newAvailable = new SortedSet<string>();
                var newActive = new SortedSet<string>();
                var newDetails = new Dictionary<string, MonitorInfo>();

                // Strip ANSI escape codes
                output = AnsiEscapeRegex().Replace(output, "");

                string? currentConnector = null;
                bool isConnected = false;
                bool isEnabled = false;

                foreach (var line in output.Split('\n'))
                {
                    var headerMatch = OutputHeaderRegex().Match(line);
                    if (headerMatch.Success)
                    {
                        // Save previous output state
                        if (currentConnector != null && isConnected)
                        {
                            var monitorInfo = DrmEdidHelper.GetMonitorInfoForConnector(currentConnector, edidMap, _logger);
                            newAvailable.Add(monitorInfo.Name);
                            newDetails.TryAdd(monitorInfo.Name, monitorInfo);
                            if (isEnabled)
                                newActive.Add(monitorInfo.Name);
                        }

                        currentConnector = headerMatch.Groups[1].Value;
                        isConnected = false;
                        isEnabled = false;
                        continue;
                    }

                    var trimmed = line.Trim();
                    if (trimmed == "connected")
                        isConnected = true;
                    else if (trimmed == "enabled")
                        isEnabled = true;
                }

                // Handle the last output
                if (currentConnector != null && isConnected)
                {
                    var monitorInfo = DrmEdidHelper.GetMonitorInfoForConnector(currentConnector, edidMap, _logger);
                    newAvailable.Add(monitorInfo.Name);
                    newDetails.TryAdd(monitorInfo.Name, monitorInfo);
                    if (isEnabled)
                        newActive.Add(monitorInfo.Name);
                }

                MonitorDetails = newDetails;

                // Fire events if changed
                if (!AvailableMonitors.SetEquals(newAvailable))
                {
                    AvailableMonitors = newAvailable;
                    _logger.LogDebug("Available monitors updated: {Monitors}", string.Join(", ", newAvailable));
                    AvailableMonitorsUpdated?.Invoke();
                }

                if (!ActiveMonitors.SetEquals(newActive))
                {
                    ActiveMonitors = newActive;
                    _logger.LogDebug("Active monitors updated: {Monitors}", string.Join(", ", newActive));
                    ActiveMonitorsUpdated?.Invoke();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error polling display state");
            }
        }

        [GeneratedRegex(@"\x1b\[[0-9;]*m")]
        private static partial Regex AnsiEscapeRegex();

        private static string? RunKScreenDoctor()
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "kscreen-doctor",
                Arguments = "--outputs",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return null;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(TimeSpan.FromSeconds(5));
            return output;
        }

        public void Dispose()
        {
            _pollTimer.Dispose();
        }
    }
}
