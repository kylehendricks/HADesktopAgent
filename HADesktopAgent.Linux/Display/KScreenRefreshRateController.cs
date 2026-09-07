using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using HADesktopAgent.Core.Display;
using Microsoft.Extensions.Logging;

namespace HADesktopAgent.Linux.Display
{
    /// <summary>
    /// Controls refresh rates via kscreen-doctor. Monitors are resolved from their
    /// friendly (EDID) name to a DRM connector, then to one of the modes kscreen
    /// reports for that connector.
    /// </summary>
    /// <remarks>
    /// Modes are set by kscreen's own mode id rather than by "WxH@rate", because
    /// kscreen reports fractional rates (143.99, 59.94) that would not round-trip
    /// through the integer rates this interface deals in.
    /// </remarks>
    public sealed partial class KScreenRefreshRateController : IRefreshRateController
    {
        /// <summary>
        /// Collapses the burst of queries that arrives each time the display watcher
        /// polls: every RefreshRateSelect asks for both its rates and its current rate,
        /// so without this each 5s poll would spawn kscreen-doctor twice per monitor.
        /// </summary>
        private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(1);

        private readonly ILogger<KScreenRefreshRateController> _logger;

        private readonly object _cacheLock = new();
        private Dictionary<string, OutputState>? _cachedOutputs;
        private DateTime _cachedAt;

        public KScreenRefreshRateController(ILogger<KScreenRefreshRateController> logger)
        {
            _logger = logger;
        }

        private sealed record DisplayMode(int Id, int Width, int Height, double Rate, bool IsCurrent);

        private sealed record OutputState(bool Enabled, List<DisplayMode> Modes)
        {
            public DisplayMode? CurrentMode => Modes.FirstOrDefault(m => m.IsCurrent);
        }

        // Matches "Output: <id> <connector> <uuid>"
        [GeneratedRegex(@"^Output:\s+\d+\s+(\S+)")]
        private static partial Regex OutputHeaderRegex();

        // Matches one mode token, e.g. "34:5120x1440@143.99*" — trailing '*' marks the
        // current mode and '!' the preferred one.
        [GeneratedRegex(@"(\d+):(\d+)x(\d+)@(\d+(?:\.\d+)?)([*!]*)")]
        private static partial Regex ModeRegex();

        [GeneratedRegex(@"\x1b\[[0-9;]*m")]
        private static partial Regex AnsiEscapeRegex();

        public List<int> GetAvailableRefreshRates(string monitorName)
        {
            if (!TryGetOutput(monitorName, out var output) || !output.Enabled)
                return [];

            var current = output.CurrentMode;
            if (current == null)
                return [];

            // Only rates valid at the current resolution, matching the Windows behaviour.
            var rates = new SortedSet<int>();
            foreach (var mode in output.Modes)
            {
                if (mode.Width == current.Width && mode.Height == current.Height)
                {
                    rates.Add(RoundRate(mode.Rate));
                }
            }

            return [.. rates];
        }

        public int? GetCurrentRefreshRate(string monitorName)
        {
            if (!TryGetOutput(monitorName, out var output) || !output.Enabled)
                return null;

            var current = output.CurrentMode;
            return current == null ? null : RoundRate(current.Rate);
        }

        public bool SetRefreshRate(string monitorName, int refreshRate)
        {
            var connector = DrmEdidHelper.GetConnectorForFriendlyName(monitorName, _logger);
            if (connector == null)
            {
                _logger.LogWarning("Cannot set refresh rate — no connector for monitor '{Monitor}'", monitorName);
                return false;
            }

            if (!TryGetOutput(monitorName, out var output) || !output.Enabled)
            {
                _logger.LogWarning("Cannot set refresh rate — monitor '{Monitor}' is not active", monitorName);
                return false;
            }

            var current = output.CurrentMode;
            if (current == null)
            {
                _logger.LogWarning("Cannot set refresh rate — no current mode for '{Monitor}'", monitorName);
                return false;
            }

            // Several modes can round to the same integer rate (3840x2160 offers both
            // 60.00 and 59.94); pick whichever sits closest to what was asked for.
            var target = output.Modes
                .Where(m => m.Width == current.Width
                         && m.Height == current.Height
                         && RoundRate(m.Rate) == refreshRate)
                .OrderBy(m => Math.Abs(m.Rate - refreshRate))
                .FirstOrDefault();

            if (target == null)
            {
                _logger.LogWarning(
                    "Refresh rate {Rate}Hz is not available on '{Monitor}' at {Width}x{Height}",
                    refreshRate, monitorName, current.Width, current.Height);
                return false;
            }

            var arguments = $"output.{connector}.mode.{target.Id}";
            _logger.LogInformation(
                "Setting {Monitor} ({Connector}) to {Rate}Hz: kscreen-doctor {Args}",
                monitorName, connector, refreshRate, arguments);

            var succeeded = RunKScreenDoctor(arguments, out var stderr);
            if (!succeeded)
            {
                _logger.LogError("kscreen-doctor failed to set refresh rate: {Stderr}", stderr);
            }

            InvalidateCache();
            return succeeded;
        }

        private bool TryGetOutput(string monitorName, out OutputState output)
        {
            output = null!;

            var connector = DrmEdidHelper.GetConnectorForFriendlyName(monitorName, _logger);
            if (connector == null)
            {
                return false;
            }

            var outputs = GetOutputs();
            return outputs != null && outputs.TryGetValue(connector, out output!);
        }

        private Dictionary<string, OutputState>? GetOutputs()
        {
            lock (_cacheLock)
            {
                if (_cachedOutputs != null && DateTime.UtcNow - _cachedAt < CacheTtl)
                {
                    return _cachedOutputs;
                }
            }

            var parsed = QueryOutputs();

            lock (_cacheLock)
            {
                _cachedOutputs = parsed;
                _cachedAt = DateTime.UtcNow;
            }

            return parsed;
        }

        private void InvalidateCache()
        {
            lock (_cacheLock)
            {
                _cachedOutputs = null;
            }
        }

        private Dictionary<string, OutputState>? QueryOutputs()
        {
            if (!RunKScreenDoctor("--outputs", out var stderr, out var stdout))
            {
                _logger.LogError("kscreen-doctor --outputs failed: {Stderr}", stderr);
                return null;
            }

            return ParseOutputs(AnsiEscapeRegex().Replace(stdout, ""));
        }

        private static Dictionary<string, OutputState> ParseOutputs(string output)
        {
            var outputs = new Dictionary<string, OutputState>();

            string? connector = null;
            var enabled = false;
            var modes = new List<DisplayMode>();

            void Commit()
            {
                if (connector != null)
                {
                    outputs[connector] = new OutputState(enabled, modes);
                }
            }

            foreach (var line in output.Split('\n'))
            {
                var headerMatch = OutputHeaderRegex().Match(line);
                if (headerMatch.Success)
                {
                    Commit();

                    connector = headerMatch.Groups[1].Value;
                    enabled = false;
                    modes = [];
                    continue;
                }

                var trimmed = line.Trim();
                if (trimmed == "enabled")
                {
                    enabled = true;
                }
                else if (trimmed.StartsWith("Modes:", StringComparison.Ordinal))
                {
                    foreach (Match match in ModeRegex().Matches(trimmed))
                    {
                        modes.Add(new DisplayMode(
                            Id: int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                            Width: int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                            Height: int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture),
                            Rate: double.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture),
                            IsCurrent: match.Groups[5].Value.Contains('*')));
                    }
                }
            }

            Commit();
            return outputs;
        }

        private static int RoundRate(double rate) =>
            (int)Math.Round(rate, MidpointRounding.AwayFromZero);

        private static bool RunKScreenDoctor(string arguments, out string stderr) =>
            RunKScreenDoctor(arguments, out stderr, out _);

        private static bool RunKScreenDoctor(string arguments, out string stderr, out string stdout)
        {
            stderr = "";
            stdout = "";

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
                return false;
            }

            stdout = process.StandardOutput.ReadToEnd();
            stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(TimeSpan.FromSeconds(10));

            return process.ExitCode == 0;
        }
    }
}
