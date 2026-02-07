using System.Diagnostics;
using System.Text.RegularExpressions;
using HADesktopAgent.Core.Audio;
using Microsoft.Extensions.Logging;

namespace HADesktopAgent.Linux.Audio
{
    public sealed partial class PulseAudioManager : IAudioManager, IDisposable
    {
        public event EventHandler? DeviceChanged;

        private readonly ILogger<PulseAudioManager> _logger;
        private readonly CancellationTokenSource _cts = new();
        private Process? _subscribeProcess;
        private Timer? _debounceTimer;

        [GeneratedRegex(@"^Sink #(\d+)")]
        private static partial Regex SinkHeaderRegex();

        [GeneratedRegex(@"^\tName:\s*(.+)")]
        private static partial Regex SinkNameRegex();

        [GeneratedRegex(@"^\tDescription:\s*(.+)")]
        private static partial Regex SinkDescriptionRegex();

        public PulseAudioManager(ILogger<PulseAudioManager> logger)
        {
            _logger = logger;
            Task.Run(() => SubscribeToChanges(_cts.Token));
        }

        public List<AudioDevice> GetAudioDevices()
        {
            var devices = new List<AudioDevice>();

            try
            {
                var defaultSink = RunPactl("get-default-sink")?.Trim();
                var sinkOutput = RunPactl("list sinks");

                if (sinkOutput == null)
                    return devices;

                string? currentName = null;
                string? currentDescription = null;

                foreach (var line in sinkOutput.Split('\n'))
                {
                    if (SinkHeaderRegex().IsMatch(line))
                    {
                        // Save previous sink if we have one
                        if (currentName != null && currentDescription != null)
                        {
                            devices.Add(new AudioDevice
                            {
                                Id = currentName,
                                FriendlyName = currentDescription,
                                UserFriendlyName = currentDescription,
                                DeviceName = currentName,
                                IsActive = currentName == defaultSink
                            });
                        }

                        currentName = null;
                        currentDescription = null;
                        continue;
                    }

                    var nameMatch = SinkNameRegex().Match(line);
                    if (nameMatch.Success)
                    {
                        currentName = nameMatch.Groups[1].Value;
                        continue;
                    }

                    var descMatch = SinkDescriptionRegex().Match(line);
                    if (descMatch.Success)
                    {
                        currentDescription = descMatch.Groups[1].Value;
                    }
                }

                // Don't forget the last sink
                if (currentName != null && currentDescription != null)
                {
                    devices.Add(new AudioDevice
                    {
                        Id = currentName,
                        FriendlyName = currentDescription,
                        UserFriendlyName = currentDescription,
                        DeviceName = currentName,
                        IsActive = currentName == defaultSink
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enumerate audio devices");
            }

            return devices;
        }

        public void SetActiveDevice(string deviceId)
        {
            try
            {
                _logger.LogInformation("Setting default audio sink to {DeviceId}", deviceId);
                RunPactl($"set-default-sink {deviceId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set default sink to {DeviceId}", deviceId);
            }
        }

        private async Task SubscribeToChanges(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    _logger.LogDebug("Starting pactl subscribe");

                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "pactl",
                        Arguments = "subscribe",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    _subscribeProcess = Process.Start(startInfo);
                    if (_subscribeProcess == null)
                    {
                        _logger.LogError("Failed to start pactl subscribe process");
                        await Task.Delay(TimeSpan.FromSeconds(10), ct);
                        continue;
                    }

                    var reader = _subscribeProcess.StandardOutput;

                    while (!ct.IsCancellationRequested && !_subscribeProcess.HasExited)
                    {
                        var line = await reader.ReadLineAsync(ct);
                        if (line == null) break;

                        // pactl subscribe outputs lines like:
                        // Event 'change' on sink #53
                        // Event 'new' on sink #58
                        // Event 'remove' on sink #58
                        if (line.Contains("on sink") || line.Contains("on server"))
                        {
                            DebouncedDeviceChanged();
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in pactl subscribe watcher");
                }

                if (!ct.IsCancellationRequested)
                {
                    _logger.LogWarning("pactl subscribe process exited, restarting in 5 seconds");
                    await Task.Delay(TimeSpan.FromSeconds(5), ct);
                }
            }
        }

        private void DebouncedDeviceChanged()
        {
            _debounceTimer?.Dispose();
            _debounceTimer = new Timer(_ =>
            {
                _logger.LogDebug("Audio device change detected");
                DeviceChanged?.Invoke(this, EventArgs.Empty);
            }, null, TimeSpan.FromMilliseconds(500), Timeout.InfiniteTimeSpan);
        }

        private static string? RunPactl(string arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "pactl",
                Arguments = arguments,
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
            _cts.Cancel();
            _debounceTimer?.Dispose();
            if (_subscribeProcess is { HasExited: false })
            {
                _subscribeProcess.Kill();
                _subscribeProcess.Dispose();
            }
            _cts.Dispose();
        }
    }
}
