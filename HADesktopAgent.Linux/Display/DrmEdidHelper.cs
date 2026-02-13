using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using HADesktopAgent.Core.Display;
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
        /// MonitorInfo records containing display names and EDID identifiers.
        /// Falls back to the connector name if EDID parsing fails.
        /// </summary>
        public static Dictionary<string, MonitorInfo> GetConnectorToMonitorInfoMap(ILogger? logger = null)
        {
            var map = new Dictionary<string, MonitorInfo>();

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

                byte[] edidBytes;
                try
                {
                    edidBytes = File.ReadAllBytes(edidPath);
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "Failed to read EDID from {EdidPath}", edidPath);
                    continue;
                }

                if (edidBytes.Length == 0)
                    continue;

                var friendlyName = ParseEdidProductName(edidPath, logger) ?? connectorName;
                var edidIdentifier = ParseEdidIdentifier(edidBytes, logger);

                map.TryAdd(connectorName, new MonitorInfo
                {
                    Name = friendlyName,
                    EdidIdentifier = edidIdentifier
                });
            }

            return map;
        }

        /// <summary>
        /// Gets the connector name for a given friendly name, or null if not found.
        /// </summary>
        public static string? GetConnectorForFriendlyName(string friendlyName, ILogger? logger = null)
        {
            var map = GetConnectorToMonitorInfoMap(logger);
            return map.FirstOrDefault(kvp => kvp.Value.Name == friendlyName).Key;
        }

        /// <summary>
        /// Gets the MonitorInfo for a given connector name, falling back to the connector name as display name.
        /// </summary>
        public static MonitorInfo GetMonitorInfoForConnector(string connectorName, Dictionary<string, MonitorInfo>? cachedMap = null, ILogger? logger = null)
        {
            var map = cachedMap ?? GetConnectorToMonitorInfoMap(logger);
            return map.GetValueOrDefault(connectorName, new MonitorInfo { Name = connectorName });
        }

        /// <summary>
        /// Parses the EDID binary to build a cross-platform identifier string.
        /// Format: "MFG-PRODUCT" or "MFG-PRODUCT-SERIAL" (e.g., "SAM-7796-HNTXA00720").
        /// </summary>
        internal static string? ParseEdidIdentifier(byte[] edidBytes, ILogger? logger = null)
        {
            if (edidBytes.Length < 128)
                return null;

            try
            {
                // Bytes 8-9: Manufacturer ID (big-endian, 3 compressed ASCII letters)
                var manufacturer = DecodeManufacturer(edidBytes[8], edidBytes[9]);

                // Bytes 10-11: Product code (little-endian)
                var productCode = (ushort)(edidBytes[10] | (edidBytes[11] << 8));

                // Try to find serial string from descriptor blocks (bytes 54-125)
                var serialString = ParseEdidDescriptorString(edidBytes, 0xFF);

                if (!string.IsNullOrEmpty(serialString))
                {
                    return $"{manufacturer}-{productCode:X4}-{serialString}";
                }

                // Fall back to 32-bit serial number (bytes 12-15, little-endian)
                var serialNumber = (uint)(edidBytes[12] | (edidBytes[13] << 8) | (edidBytes[14] << 16) | (edidBytes[15] << 24));
                if (serialNumber != 0)
                {
                    return $"{manufacturer}-{productCode:X4}-{serialNumber}";
                }

                // No serial available — return just MFG-PRODUCT
                return $"{manufacturer}-{productCode:X4}";
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to parse EDID identifier");
                return null;
            }
        }

        /// <summary>
        /// Decodes the 2-byte EDID manufacturer ID into a 3-letter PNP ID code.
        /// </summary>
        internal static string DecodeManufacturer(byte b0, byte b1)
        {
            var val = (b0 << 8) | b1;
            var c1 = (char)(((val >> 10) & 0x1F) + 'A' - 1);
            var c2 = (char)(((val >> 5) & 0x1F) + 'A' - 1);
            var c3 = (char)((val & 0x1F) + 'A' - 1);
            return new string([c1, c2, c3]);
        }

        /// <summary>
        /// Parses an 18-byte descriptor string from the EDID descriptor blocks.
        /// Tag 0xFF = serial string, 0xFC = product name, 0xFE = unspecified text.
        /// </summary>
        private static string? ParseEdidDescriptorString(byte[] edidBytes, byte tag)
        {
            for (int i = 0; i < 4; i++)
            {
                var offset = 54 + i * 18;
                if (offset + 18 > edidBytes.Length)
                    break;

                // Check for descriptor block (first two bytes are 0)
                if (edidBytes[offset] != 0 || edidBytes[offset + 1] != 0)
                    continue;

                if (edidBytes[offset + 3] != tag)
                    continue;

                // Descriptor string is bytes 5-17 (13 bytes), terminated by newline
                var str = Encoding.ASCII.GetString(edidBytes, offset + 5, 13).TrimEnd('\n', ' ', '\0');
                return string.IsNullOrEmpty(str) ? null : str;
            }

            return null;
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
