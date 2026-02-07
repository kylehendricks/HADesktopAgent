using System.Runtime.InteropServices;
using HADesktopAgent.Core.Audio;

namespace HADesktopAgent.Windows.Audio
{
    public sealed class AudioDeviceManager : IAudioManager, IDisposable
    {
        private readonly IMMDeviceEnumerator _enumerator;
        private readonly IMMNotificationClient _notificationClient;
        private readonly ILogger<AudioDeviceManager> _logger;

        public event EventHandler? DeviceChanged;

        public AudioDeviceManager(ILogger<AudioDeviceManager> logger)
        {
            _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            _notificationClient = new NotificationClient(this);
            _enumerator.RegisterEndpointNotificationCallback(_notificationClient);
            _logger = logger;
        }

        private void OnDeviceChanged()
        {
            DeviceChanged?.Invoke(this, EventArgs.Empty);
        }

        private class NotificationClient(AudioDeviceManager manager) : IMMNotificationClient
        {
            private readonly AudioDeviceManager _manager = manager;

            public void OnDeviceStateChanged(string deviceId, int newState)
            {
                _manager.OnDeviceChanged();
            }

            public void OnDeviceAdded(string deviceId)
            {
                _manager.OnDeviceChanged();
            }

            public void OnDeviceRemoved(string deviceId)
            {
                _manager.OnDeviceChanged();
            }

            public void OnDefaultDeviceChanged(EDataFlow flow, ERole role, string defaultDeviceId)
            {
                if (flow == EDataFlow.eRender)
                {
                    _manager.OnDeviceChanged();
                }
            }

            public void OnPropertyValueChanged(string deviceId, PropertyKey key)
            {
            }
        }

        public List<AudioDevice> GetAudioDevices()
        {
            var devices = new List<AudioDevice>();
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();

            try
            {
                // Get default device ID
                enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out var defaultDevice);
                defaultDevice.GetId(out var defaultIdPtr);
                var defaultId = Marshal.PtrToStringUni(defaultIdPtr);
                Marshal.FreeCoTaskMem(defaultIdPtr);
                Marshal.ReleaseComObject(defaultDevice);

                // Enumerate all devices
                enumerator.EnumerateAudioEndPoints(EDataFlow.eRender, DeviceState.Active, out var collection);
                collection.GetCount(out int count);

                for (int i = 0; i < count; i++)
                {
                    collection.Item(i, out var device);

                    // Get device ID
                    device.GetId(out var deviceIdPtr);
                    var deviceId = Marshal.PtrToStringUni(deviceIdPtr) ?? "Unknown";
                    Marshal.FreeCoTaskMem(deviceIdPtr);

                    // Get device friendly name
                    device.OpenPropertyStore(0, out var props);
                    var friendlyNameKey = new PropertyKey
                    {
                        fmtid = new Guid(0xa45c254e, 0xdf1c, 0x4efd, 0x80, 0x20, 0x67, 0xd1, 0x46, 0xa8, 0x50, 0xe0),
                        pid = 14
                    };
                    props.GetValue(ref friendlyNameKey, out var friendlyNameValue);
                    string friendlyName = Marshal.PtrToStringUni(friendlyNameValue.pwszVal) ?? "Unknown Device";

                    // Get device interface friendly name
                    var interfaceKey = new PropertyKey
                    {
                        fmtid = new Guid(0x026e516e, 0xb814, 0x414b, 0x83, 0xcd, 0x85, 0x6d, 0x6f, 0xef, 0x48, 0x22),
                        pid = 2
                    };
                    props.GetValue(ref interfaceKey, out var interfaceValue);
                    var deviceName = Marshal.PtrToStringUni(interfaceValue.pwszVal) ?? "";

                    // Parse out the user-friendly name
                    string userFriendlyName = friendlyName;
                    int parenIndex = friendlyName.LastIndexOf('(');
                    if (parenIndex > 0)
                    {
                        userFriendlyName = friendlyName.Substring(0, parenIndex).Trim();
                    }

                    devices.Add(new AudioDevice
                    {
                        Id = deviceId,
                        FriendlyName = friendlyName,
                        UserFriendlyName = userFriendlyName,
                        DeviceName = deviceName,
                        IsActive = deviceId == defaultId
                    });

                    Marshal.ReleaseComObject(props);
                    Marshal.ReleaseComObject(device);
                }

                Marshal.ReleaseComObject(collection);
            }
            finally
            {
                Marshal.ReleaseComObject(enumerator);
            }

            return devices;
        }

        public void SetActiveDevice(string deviceId)
        {
            var policyConfig = (IPolicyConfig)new PolicyConfigClient();

            try
            {
                policyConfig.SetDefaultEndpoint(deviceId, ERole.eConsole);
                policyConfig.SetDefaultEndpoint(deviceId, ERole.eMultimedia);
                policyConfig.SetDefaultEndpoint(deviceId, ERole.eCommunications);
            }
            finally
            {
                Marshal.ReleaseComObject(policyConfig);
            }
        }

        public void Dispose()
        {
            if (_enumerator != null && _notificationClient != null)
            {
                _enumerator.UnregisterEndpointNotificationCallback(_notificationClient);
                Marshal.ReleaseComObject(_enumerator);
            }
        }
    }

    #region Windows API
    // COM Interfaces
    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumerator { }

    [ComImport]
    [Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    class PolicyConfigClient { }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        void EnumerateAudioEndPoints(EDataFlow dataFlow, DeviceState stateMask, out IMMDeviceCollection devices);
        void GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice device);
        void GetDevice(string id, out IMMDevice device);
        void RegisterEndpointNotificationCallback(IMMNotificationClient client);
        void UnregisterEndpointNotificationCallback(IMMNotificationClient client);
    }

    [ComImport]
    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        void GetCount(out int count);
        void Item(int index, out IMMDevice device);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        void Activate(ref Guid id, int clsCtx, nint activationParams, out nint interfacePointer);
        void OpenPropertyStore(int access, out IPropertyStore properties);
        void GetId(out nint id);
        void GetState(out int state);
    }

    [ComImport]
    [Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        void GetCount(out int count);
        void GetAt(int prop, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
        void SetValue(ref PropertyKey key, ref PropVariant value);
        void Commit();
    }

    [ComImport]
    [Guid("f8679f50-850a-41cf-9c72-430f290290c8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfig
    {
        void GetMixFormat(string deviceId, nint format);
        void GetDeviceFormat(string deviceId, bool defaultFormat, nint format);
        void ResetDeviceFormat(string deviceId);
        void SetDeviceFormat(string deviceId, nint endpointFormat, nint mixFormat);
        void GetProcessingPeriod(string deviceId, bool defaultPeriod, out long period1, out long period2);
        void SetProcessingPeriod(string deviceId, long period);
        void GetShareMode(string deviceId, out int shareMode);
        void SetShareMode(string deviceId, int shareMode);
        void GetPropertyValue(string deviceId, ref PropertyKey key, out PropVariant value);
        void SetPropertyValue(string deviceId, ref PropertyKey key, ref PropVariant value);
        void SetDefaultEndpoint(string deviceId, ERole role);
        void SetEndpointVisibility(string deviceId, bool visible);
    }

    [ComImport]
    [Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMNotificationClient
    {
        void OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int newState);
        void OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        void OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        void OnDefaultDeviceChanged(EDataFlow flow, ERole role, [MarshalAs(UnmanagedType.LPWStr)] string defaultDeviceId);
        void OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, PropertyKey key);
    }

    enum EDataFlow
    {
        eRender = 0,
        eCapture = 1,
        eAll = 2
    }

    enum ERole
    {
        eConsole = 0,
        eMultimedia = 1,
        eCommunications = 2
    }

    [Flags]
    enum DeviceState
    {
        Active = 0x1,
        Disabled = 0x2,
        NotPresent = 0x4,
        Unplugged = 0x8,
        All = 0xF
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PropertyKey
    {
        public Guid fmtid;
        public int pid;
    }

    [StructLayout(LayoutKind.Explicit)]
    struct PropVariant
    {
        [FieldOffset(0)] public short vt;
        [FieldOffset(8)] public nint pwszVal;
    }

    #endregion
}
