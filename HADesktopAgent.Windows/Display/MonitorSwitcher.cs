using System.Runtime.InteropServices;

namespace HADesktopAgent.Windows.Display
{
    /// <summary>
    /// Simple monitor switcher - switches between individual monitors (one active at a time)
    /// </summary>
    public class MonitorSwitcher
    {
        #region Public API

        /// <summary>
        /// Represents a connected monitor
        /// </summary>
        public record Monitor
        {
            public required string Name { get; init; }
            public required bool IsActive { get; init; }
            public required string DevicePath { get; init; }
        }

        /// <summary>
        /// Gets all connected monitors
        /// </summary>
        public static List<Monitor> GetMonitors()
        {
            var monitors = new List<Monitor>();

            if (GetDisplayConfigBufferSizes(QDC_ALL_PATHS, out uint pathCount, out uint modeCount) != 0)
                return monitors;

            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];

            if (QueryDisplayConfig(QDC_ALL_PATHS, ref pathCount, paths, ref modeCount, modes, nint.Zero) != 0)
                return monitors;

            var seenPaths = new HashSet<string>();

            for (int i = 0; i < pathCount; i++)
            {
                var path = paths[i];

                if (path.targetInfo.targetAvailable == 0)
                    continue;

                var deviceName = new DISPLAYCONFIG_TARGET_DEVICE_NAME
                {
                    header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = 2,
                        size = (uint)Marshal.SizeOf(typeof(DISPLAYCONFIG_TARGET_DEVICE_NAME)),
                        adapterId = path.targetInfo.adapterId,
                        id = path.targetInfo.id
                    }
                };

                if (DisplayConfigGetDeviceInfo(ref deviceName) != 0 ||
                    string.IsNullOrEmpty(deviceName.monitorFriendlyDeviceName))
                    continue;

                if (seenPaths.Contains(deviceName.monitorDevicePath))
                    continue;

                seenPaths.Add(deviceName.monitorDevicePath);

                monitors.Add(new Monitor
                {
                    Name = deviceName.monitorFriendlyDeviceName,
                    DevicePath = deviceName.monitorDevicePath,
                    IsActive = (path.flags & DISPLAYCONFIG_PATH_ACTIVE) != 0
                });
            }

            return monitors;
        }

        /// <summary>
        /// Applies a display configuration atomically — enables the named monitors, disables everything else.
        /// </summary>
        public static bool ApplyConfiguration(ISet<string> enabledMonitors)
        {
            if (GetDisplayConfigBufferSizes(QDC_ALL_PATHS, out uint pathCount, out uint modeCount) != 0)
                return false;

            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];

            if (QueryDisplayConfig(QDC_ALL_PATHS, ref pathCount, paths, ref modeCount, modes, nint.Zero) != 0)
                return false;

            // Resolve friendly name for each path and pick the best path per monitor
            var bestPathByDevice = new Dictionary<string, (DISPLAYCONFIG_PATH_INFO path, string name)>();

            for (int i = 0; i < pathCount; i++)
            {
                var path = paths[i];
                if (path.targetInfo.targetAvailable == 0)
                    continue;

                var deviceName = new DISPLAYCONFIG_TARGET_DEVICE_NAME
                {
                    header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = 2,
                        size = (uint)Marshal.SizeOf(typeof(DISPLAYCONFIG_TARGET_DEVICE_NAME)),
                        adapterId = path.targetInfo.adapterId,
                        id = path.targetInfo.id
                    }
                };

                if (DisplayConfigGetDeviceInfo(ref deviceName) != 0 ||
                    string.IsNullOrEmpty(deviceName.monitorFriendlyDeviceName))
                    continue;

                var devPath = deviceName.monitorDevicePath;
                var friendlyName = deviceName.monitorFriendlyDeviceName;

                if (!enabledMonitors.Contains(friendlyName))
                    continue;

                // Prefer an already-active path for this device, otherwise take the first
                if (!bestPathByDevice.ContainsKey(devPath))
                {
                    bestPathByDevice[devPath] = (path, friendlyName);
                }
                else if ((path.flags & DISPLAYCONFIG_PATH_ACTIVE) != 0)
                {
                    bestPathByDevice[devPath] = (path, friendlyName);
                }
            }

            if (bestPathByDevice.Count == 0)
                return false;

            // Build new path and mode arrays
            var newPaths = new List<DISPLAYCONFIG_PATH_INFO>();
            var newModes = new List<DISPLAYCONFIG_MODE_INFO>();
            var addedModeIndices = new HashSet<uint>();

            foreach (var (_, (path, _)) in bestPathByDevice)
            {
                var p = path;
                p.flags = DISPLAYCONFIG_PATH_ACTIVE;
                p.sourceInfo.statusFlags = 0x00000001;
                p.targetInfo.statusFlags = 0x00000001;

                var sourceModeIdx = p.sourceInfo.modeInfoIdx;
                var targetModeIdx = p.targetInfo.modeInfoIdx;

                if (sourceModeIdx != 0xFFFFFFFF && sourceModeIdx < modeCount && !addedModeIndices.Contains(sourceModeIdx))
                {
                    newModes.Add(modes[sourceModeIdx]);
                    p.sourceInfo.modeInfoIdx = (uint)(newModes.Count - 1);
                    addedModeIndices.Add(sourceModeIdx);
                }
                else if (sourceModeIdx != 0xFFFFFFFF && addedModeIndices.Contains(sourceModeIdx))
                {
                    // Mode already added — find its new index
                    p.sourceInfo.modeInfoIdx = (uint)newModes.IndexOf(modes[sourceModeIdx]);
                }
                else
                {
                    p.sourceInfo.modeInfoIdx = 0xFFFFFFFF;
                }

                if (targetModeIdx != 0xFFFFFFFF && targetModeIdx < modeCount && !addedModeIndices.Contains(targetModeIdx))
                {
                    newModes.Add(modes[targetModeIdx]);
                    p.targetInfo.modeInfoIdx = (uint)(newModes.Count - 1);
                    addedModeIndices.Add(targetModeIdx);
                }
                else if (targetModeIdx != 0xFFFFFFFF && addedModeIndices.Contains(targetModeIdx))
                {
                    p.targetInfo.modeInfoIdx = (uint)newModes.IndexOf(modes[targetModeIdx]);
                }
                else
                {
                    p.targetInfo.modeInfoIdx = 0xFFFFFFFF;
                }

                newPaths.Add(p);
            }

            return SetDisplayConfig(
                (uint)newPaths.Count,
                newPaths.ToArray(),
                (uint)newModes.Count,
                newModes.ToArray(),
                SDC_APPLY | SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_ALLOW_CHANGES | SDC_SAVE_TO_DATABASE) == 0;
        }

        #endregion

        #region Windows API

        [StructLayout(LayoutKind.Sequential)]
        internal struct LUID
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct DISPLAYCONFIG_PATH_SOURCE_INFO
        {
            public LUID adapterId;
            public uint id;
            public uint modeInfoIdx;
            public uint statusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct DISPLAYCONFIG_PATH_TARGET_INFO
        {
            public LUID adapterId;
            public uint id;
            public uint modeInfoIdx;
            public uint outputTechnology;
            public uint rotation;
            public uint scaling;
            public DISPLAYCONFIG_RATIONAL refreshRate;
            public uint scanLineOrdering;
            public uint targetAvailable;
            public uint statusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct DISPLAYCONFIG_RATIONAL
        {
            public uint Numerator;
            public uint Denominator;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct DISPLAYCONFIG_PATH_INFO
        {
            public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
            public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
            public uint flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct DISPLAYCONFIG_MODE_INFO
        {
            public uint infoType;
            public uint id;
            public LUID adapterId;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 48)]
            public byte[] modeInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct DISPLAYCONFIG_DEVICE_INFO_HEADER
        {
            public uint type;
            public uint size;
            public LUID adapterId;
            public uint id;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DISPLAYCONFIG_TARGET_DEVICE_NAME
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
            public DISPLAYCONFIG_TARGET_DEVICE_NAME_FLAGS flags;
            public uint outputTechnology;
            public ushort edidManufactureId;
            public ushort edidProductCodeId;
            public uint connectorInstance;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string monitorFriendlyDeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string monitorDevicePath;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct DISPLAYCONFIG_TARGET_DEVICE_NAME_FLAGS
        {
            public uint value;
        }

        [DllImport("user32.dll")]
        static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

        [DllImport("user32.dll")]
        static extern int QueryDisplayConfig(uint flags, ref uint numPathArrayElements, [In, Out] DISPLAYCONFIG_PATH_INFO[] pathArray,
            ref uint numModeInfoArrayElements, [In, Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray, nint currentTopologyId);

        [DllImport("user32.dll")]
        static extern int SetDisplayConfig(uint numPathArrayElements, [In] DISPLAYCONFIG_PATH_INFO[] pathArray,
            uint numModeInfoArrayElements, [In] DISPLAYCONFIG_MODE_INFO[] modeInfoArray, uint flags);

        [DllImport("user32.dll")]
        static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME deviceName);

        const uint QDC_ALL_PATHS = 0x00000001;
        const uint SDC_APPLY = 0x00000080;
        const uint SDC_USE_SUPPLIED_DISPLAY_CONFIG = 0x00000020;
        const uint SDC_ALLOW_CHANGES = 0x00000400;
        const uint SDC_SAVE_TO_DATABASE = 0x00000200;
        const uint DISPLAYCONFIG_PATH_ACTIVE = 0x00000001;

        #endregion
    }
}
