using HADesktopAgent.Core.Entity;
using Microsoft.Extensions.Logging;

namespace HADesktopAgent.Core.Audio.Entity
{
    public class AudioSelect : IHaStatefulEntity, IHaCommandableEntity, IHaSelectableEntity, IDisposable
    {
        public string Name => "audio";

        public string PrettyName => "Audio";

        public string UniqueId => "audio";

        public string Icon => "mdi:volume-high";
        public bool Optimistic => false;

        public string EntityType => "select";

        public SortedSet<string> Options => _audioDeviceNames;

        public string? State => _activeAudioDevice;

        private readonly IAudioManager _audioManager;
        private readonly ILogger<AudioSelect> _logger;
        private readonly Dictionary<string, string> _audioNameMappings;

        private SortedSet<string> _audioDeviceNames = [];
        private string? _activeAudioDevice;

        public event IHaStatefulEntity.StateUpdatedHandler? StateUpdated;
        public event IHaEntity.ConfigUpdatedHandler? ConfigUpdated;

        public AudioSelect(ILogger<AudioSelect> logger, IAudioManager audioManager, Dictionary<string, string>? audioNameMappings = null)
        {
            _logger = logger;
            _audioManager = audioManager;
            _audioNameMappings = audioNameMappings ?? new();
            UpdateAudioDevices();

            _audioManager.DeviceChanged += AudioDevicesChanged;
        }

        public void HandleCommand(string command)
        {
            // The command contains the display name (which may be a mapped name).
            // We need to find the original device by checking both mapped and unmapped names.
            var audioDevices = _audioManager.GetAudioDevices();
            var audioDevice = audioDevices.Find(a => GetDisplayName(a) == command);

            if (audioDevice == null)
            {
                _logger.LogWarning("Audio device '{Command}' not found", command);
                return;
            }

            _audioManager.SetActiveDevice(audioDevice.Id);
        }

        /// <summary>
        /// Gets the display name for an audio device, applying name mappings if configured.
        /// Checks the device's UserFriendlyName, FriendlyName, and DeviceName against the mapping keys.
        /// </summary>
        private string GetDisplayName(AudioDevice device)
        {
            // Try matching by UserFriendlyName
            if (_audioNameMappings.TryGetValue(device.UserFriendlyName, out var mappedByUserFriendly))
                return mappedByUserFriendly;

            // Try matching by FriendlyName
            if (_audioNameMappings.TryGetValue(device.FriendlyName, out var mappedByFriendly))
                return mappedByFriendly;

            // Try matching by DeviceName
            if (_audioNameMappings.TryGetValue(device.DeviceName, out var mappedByDevice))
                return mappedByDevice;

            // No mapping found, use original name
            return device.UserFriendlyName;
        }

        private void UpdateAudioDevices()
        {
            var audioDevices = _audioManager.GetAudioDevices();
            var audioDeviceNames = audioDevices.Select(m => GetDisplayName(m));
            var activeAudioDevice = audioDevices.Find(m => m.IsActive);
            var activeDisplayName = activeAudioDevice != null ? GetDisplayName(activeAudioDevice) : null;

            if (!_audioDeviceNames.SetEquals(audioDeviceNames))
            {
                _audioDeviceNames = [.. audioDeviceNames];
                ConfigUpdated?.Invoke(this);
            }

            if (_activeAudioDevice != activeDisplayName)
            {
                _activeAudioDevice = activeDisplayName;
                StateUpdated?.Invoke(this);
            }
        }

        private void AudioDevicesChanged(object? sender, EventArgs e)
        {
            UpdateAudioDevices();
        }

        public void Dispose()
        {
            _audioManager.DeviceChanged -= AudioDevicesChanged;
        }
    }
}
