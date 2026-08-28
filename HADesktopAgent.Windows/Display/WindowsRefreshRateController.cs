using System.Runtime.InteropServices;
using HADesktopAgent.Core.Display;

namespace HADesktopAgent.Windows.Display
{
    /// <summary>
    /// Controls refresh rates via the GDI display settings API. Monitors are resolved
    /// from their friendly name to a GDI adapter name (\\.\DISPLAYn) by matching the
    /// monitor device interface path from the display config API.
    /// </summary>
    public class WindowsRefreshRateController : IRefreshRateController
    {
        private readonly ILogger<WindowsRefreshRateController> _logger;

        public WindowsRefreshRateController(ILogger<WindowsRefreshRateController> logger)
        {
            _logger = logger;
        }

        public List<int> GetAvailableRefreshRates(string monitorName)
        {
            var gdiDeviceName = GetGdiDeviceName(monitorName);
            if (gdiDeviceName == null)
                return [];

            var current = CreateDevMode();
            if (!EnumDisplaySettingsW(gdiDeviceName, ENUM_CURRENT_SETTINGS, ref current))
                return [];

            // Only offer rates valid at the current resolution
            var rates = new SortedSet<int>();
            var mode = CreateDevMode();
            for (int i = 0; EnumDisplaySettingsW(gdiDeviceName, i, ref mode); i++)
            {
                if (mode.dmPelsWidth == current.dmPelsWidth &&
                    mode.dmPelsHeight == current.dmPelsHeight &&
                    mode.dmDisplayFrequency > 1) // 0/1 mean "hardware default", not a real rate
                {
                    rates.Add((int)mode.dmDisplayFrequency);
                }
            }

            return [.. rates];
        }

        public int? GetCurrentRefreshRate(string monitorName)
        {
            var gdiDeviceName = GetGdiDeviceName(monitorName);
            if (gdiDeviceName == null)
                return null;

            var current = CreateDevMode();
            if (!EnumDisplaySettingsW(gdiDeviceName, ENUM_CURRENT_SETTINGS, ref current))
                return null;

            return current.dmDisplayFrequency > 1 ? (int)current.dmDisplayFrequency : null;
        }

        public bool SetRefreshRate(string monitorName, int refreshRate)
        {
            var gdiDeviceName = GetGdiDeviceName(monitorName);
            if (gdiDeviceName == null)
            {
                _logger.LogWarning("Cannot set refresh rate — no active GDI device for monitor '{Monitor}'", monitorName);
                return false;
            }

            var devMode = CreateDevMode();
            if (!EnumDisplaySettingsW(gdiDeviceName, ENUM_CURRENT_SETTINGS, ref devMode))
            {
                _logger.LogWarning("Cannot read current display settings for '{Monitor}' ({GdiDevice})", monitorName, gdiDeviceName);
                return false;
            }

            devMode.dmDisplayFrequency = (uint)refreshRate;
            devMode.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY;

            var result = ChangeDisplaySettingsExW(gdiDeviceName, ref devMode, nint.Zero, CDS_UPDATEREGISTRY, nint.Zero);
            if (result != DISP_CHANGE_SUCCESSFUL)
            {
                _logger.LogWarning(
                    "ChangeDisplaySettingsEx failed for '{Monitor}' at {Rate}Hz (result: {Result})",
                    monitorName, refreshRate, result);
                return false;
            }

            _logger.LogInformation("Set '{Monitor}' to {Rate}Hz", monitorName, refreshRate);
            return true;
        }

        /// <summary>
        /// Resolves a monitor friendly name (e.g. "LG TV SSCR2") to its GDI adapter
        /// device name (e.g. \\.\DISPLAY1). Returns null if the monitor is not found
        /// or not attached to an active adapter.
        /// </summary>
        private string? GetGdiDeviceName(string monitorName)
        {
            var monitor = MonitorSwitcher.GetMonitors().FirstOrDefault(m => m.Name == monitorName);
            if (monitor == null)
                return null;

            var adapter = new DISPLAY_DEVICE { cb = (uint)Marshal.SizeOf<DISPLAY_DEVICE>() };
            for (uint i = 0; EnumDisplayDevicesW(null, i, ref adapter, 0); i++)
            {
                var attachedMonitor = new DISPLAY_DEVICE { cb = (uint)Marshal.SizeOf<DISPLAY_DEVICE>() };
                for (uint j = 0; EnumDisplayDevicesW(adapter.DeviceName, j, ref attachedMonitor, EDD_GET_DEVICE_INTERFACE_NAME); j++)
                {
                    // A monitor's device path is listed under every adapter source it has
                    // ever been associated with; only the entry with DISPLAY_DEVICE_ACTIVE
                    // set is the source currently driving it.
                    if ((attachedMonitor.StateFlags & DISPLAY_DEVICE_ACTIVE) != 0 &&
                        string.Equals(attachedMonitor.DeviceID, monitor.DevicePath, StringComparison.OrdinalIgnoreCase))
                    {
                        return adapter.DeviceName;
                    }
                }
            }

            return null;
        }

        private static DEVMODE CreateDevMode() => new() { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };

        #region Windows API

        const int ENUM_CURRENT_SETTINGS = -1;
        const uint EDD_GET_DEVICE_INTERFACE_NAME = 0x00000001;
        const uint DISPLAY_DEVICE_ACTIVE = 0x00000001;
        const uint DM_PELSWIDTH = 0x00080000;
        const uint DM_PELSHEIGHT = 0x00100000;
        const uint DM_DISPLAYFREQUENCY = 0x00400000;
        const uint CDS_UPDATEREGISTRY = 0x00000001;
        const int DISP_CHANGE_SUCCESSFUL = 0;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DISPLAY_DEVICE
        {
            public uint cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;
            public uint StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmDeviceName;
            public ushort dmSpecVersion;
            public ushort dmDriverVersion;
            public ushort dmSize;
            public ushort dmDriverExtra;
            public uint dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public uint dmDisplayOrientation;
            public uint dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmFormName;
            public ushort dmLogPixels;
            public uint dmBitsPerPel;
            public uint dmPelsWidth;
            public uint dmPelsHeight;
            public uint dmDisplayFlags;
            public uint dmDisplayFrequency;
            public uint dmICMMethod;
            public uint dmICMIntent;
            public uint dmMediaType;
            public uint dmDitherType;
            public uint dmReserved1;
            public uint dmReserved2;
            public uint dmPanningWidth;
            public uint dmPanningHeight;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool EnumDisplayDevicesW(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool EnumDisplaySettingsW(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int ChangeDisplaySettingsExW(string lpszDeviceName, ref DEVMODE lpDevMode, nint hwnd, uint dwflags, nint lParam);

        #endregion
    }
}
