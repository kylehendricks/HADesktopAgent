using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace HADesktopAgent.Linux.Display
{
    public static partial class DrmEdidHelper
    {
        private const string DrmPath = "/sys/class/drm";

        [GeneratedRegex(@"Display Product Name:\s*'(.+)'")]
        private static partial Regex ProductNameRegex();

        /// <summary>
        /// Builds a mapping of DRM connector names (e.g., "DP-1", "HDMI-A-1") to
        /// EDID display product names (e.g., "Odyssey G91SD", "LG TV SSCR2").
        /// Falls back to the connector name if EDID parsing fails.
        /// </summary>
        public static Dictionary<string, string> GetConnectorToFriendlyNameMap(ILogger? logger = null)
        {
            var map = new Dictionary<string, string>();

            if (!Directory.Exists(DrmPath))
                return map;

            foreach (var dir in Directory.GetDirectories(DrmPath))
            {
                var dirName = Path.GetFileName(dir);

                // Match card*-<connector> directories (e.g., "card1-DP-1", "card1-HDMI-A-1")
                var dashIndex = dirName.IndexOf('-');
                if (dashIndex < 0 || !dirName.StartsWith("card"))
                    continue;

                var connectorName = dirName[(dashIndex + 1)..];
                var edidPath = Path.Combine(dir, "edid");

                if (!File.Exists(edidPath))
                    continue;

                var edidBytes = File.ReadAllBytes(edidPath);
                if (edidBytes.Length == 0)
                    continue;

                var friendlyName = ParseEdidProductName(edidPath, logger);
                map.TryAdd(connectorName, friendlyName ?? connectorName);
            }

            return map;
        }

        /// <summary>
        /// Gets the connector name for a given friendly name, or null if not found.
        /// </summary>
        public static string? GetConnectorForFriendlyName(string friendlyName, ILogger? logger = null)
        {
            var map = GetConnectorToFriendlyNameMap(logger);
            return map.FirstOrDefault(kvp => kvp.Value == friendlyName).Key;
        }

        /// <summary>
        /// Gets the friendly name for a given connector name, falling back to the connector name itself.
        /// </summary>
        public static string GetFriendlyNameForConnector(string connectorName, Dictionary<string, string>? cachedMap = null, ILogger? logger = null)
        {
            var map = cachedMap ?? GetConnectorToFriendlyNameMap(logger);
            return map.GetValueOrDefault(connectorName, connectorName);
        }

        private static string? ParseEdidProductName(string edidPath, ILogger? logger)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "edid-decode",
                    Arguments = edidPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null) return null;

                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(TimeSpan.FromSeconds(5));

                var match = ProductNameRegex().Match(output);
                return match.Success ? match.Groups[1].Value : null;
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to parse EDID from {EdidPath}", edidPath);
                return null;
            }
        }
    }
}
