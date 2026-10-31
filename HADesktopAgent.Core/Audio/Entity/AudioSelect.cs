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
        private readonly AudioNameResolver _nameResolver;

        private readonly object _updateLock = new();

        private SortedSet<string> _audioDeviceNames = [];
        private string? _activeAudioDevice;

        public event IHaStatefulEntity.StateUpdatedHandler? StateUpdated;
        public event IHaEntity.ConfigUpdatedHandler? ConfigUpdated;

        public AudioSelect(ILogger<AudioSelect> logger, IAudioManager audioManager, AudioNameResolver nameResolver)
        {
            _logger = logger;
            _audioManager = audioManager;
            _nameResolver = nameResolver;
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

        private string GetDisplayName(AudioDevice device) => _nameResolver.Resolve(device);

        private void UpdateAudioDevices()
        {
            lock (_updateLock)
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
        }

        private void AudioDevicesChanged(object? sender, EventArgs e)
        {
            // Device-change notifications arrive on platform callback threads; an
            // exception escaping here is lost and would leave the list stale forever.
            try
            {
                UpdateAudioDevices();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh audio devices after device change");
            }
        }

        public void Dispose()
        {
            _audioManager.DeviceChanged -= AudioDevicesChanged;
        }
    }
}
