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
            internal LUID AdapterId { get; init; }
            internal uint TargetId { get; init; }
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
                    AdapterId = path.targetInfo.adapterId,
                    TargetId = path.targetInfo.id,
                    IsActive = (path.flags & DISPLAYCONFIG_PATH_ACTIVE) != 0
                });
            }

            return monitors;
        }

        /// <summary>
        /// Switches to the specified monitor (disables all others)
        /// </summary>
        public static bool SwitchToMonitor(Monitor monitor)
        {
            if (GetDisplayConfigBufferSizes(QDC_ALL_PATHS, out uint pathCount, out uint modeCount) != 0)
                return false;

            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];

            if (QueryDisplayConfig(QDC_ALL_PATHS, ref pathCount, paths, ref modeCount, modes, nint.Zero) != 0)
                return false;

            // Find target path(s)
            var targetPaths = new List<DISPLAYCONFIG_PATH_INFO>();
            for (int i = 0; i < pathCount; i++)
            {
                var path = paths[i];
                if (path.targetInfo.adapterId.LowPart == monitor.AdapterId.LowPart &&
                    path.targetInfo.adapterId.HighPart == monitor.AdapterId.HighPart &&
                    path.targetInfo.id == monitor.TargetId)
                {
                    targetPaths.Add(path);
                }
            }

            if (targetPaths.Count == 0)
                return false;

            // Use active path or first available
            var activePath = targetPaths.FirstOrDefault(p => (p.flags & DISPLAYCONFIG_PATH_ACTIVE) != 0);
            var targetPath = activePath.flags != 0 ? activePath : targetPaths[0];

            targetPath.flags = DISPLAYCONFIG_PATH_ACTIVE;
            targetPath.sourceInfo.statusFlags = 0x00000001;
            targetPath.targetInfo.statusFlags = 0x00000001;

            // Build mode info array
            var newModes = new List<DISPLAYCONFIG_MODE_INFO>();
            var addedModeIndices = new HashSet<uint>();
            uint newModeIndex = 0;

            var sourceModeIdx = targetPath.sourceInfo.modeInfoIdx;
            var targetModeIdx = targetPath.targetInfo.modeInfoIdx;

            if (sourceModeIdx != 0xFFFFFFFF && sourceModeIdx < modeCount && !addedModeIndices.Contains(sourceModeIdx))
            {
                newModes.Add(modes[sourceModeIdx]);
                targetPath.sourceInfo.modeInfoIdx = newModeIndex++;
                addedModeIndices.Add(sourceModeIdx);
            }
            else
            {
                targetPath.sourceInfo.modeInfoIdx = 0xFFFFFFFF;
            }

            if (targetModeIdx != 0xFFFFFFFF && targetModeIdx < modeCount && !addedModeIndices.Contains(targetModeIdx))
            {
                newModes.Add(modes[targetModeIdx]);
                targetPath.targetInfo.modeInfoIdx = newModeIndex++;
                addedModeIndices.Add(targetModeIdx);
            }
            else
            {
                targetPath.targetInfo.modeInfoIdx = 0xFFFFFFFF;
            }

            var newPaths = new[] { targetPath };

            return SetDisplayConfig(
                1,
                newPaths,
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
