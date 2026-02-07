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

        private SortedSet<string> _audioDeviceNames = [];
        private string? _activeAudioDevice;

        public event IHaStatefulEntity.StateUpdatedHandler? StateUpdated;
        public event IHaEntity.ConfigUpdatedHandler? ConfigUpdated;

        public AudioSelect(ILogger<AudioSelect> logger, IAudioManager audioManager)
        {
            _logger = logger;
            _audioManager = audioManager;
            UpdateAudioDevices();

            _audioManager.DeviceChanged += AudioDevicesChanged;
        }

        public void HandleCommand(string command)
        {
            var audioDevice = _audioManager.GetAudioDevices().Find(a => a.UserFriendlyName == command);

            if (audioDevice == null)
            {
                _logger.LogWarning("Audio device '{Command}' not found", command);
                return;
            }

            _audioManager.SetActiveDevice(audioDevice.Id);
        }

        private void UpdateAudioDevices()
        {
            var audioDevices = _audioManager.GetAudioDevices();
            var audioDeviceNames = audioDevices.Select(m => m.UserFriendlyName);
            var activeAudioDevice = audioDevices.Find(m => m.IsActive)?.UserFriendlyName;

            if (!_audioDeviceNames.SetEquals(audioDeviceNames))
            {
                _audioDeviceNames = [.. audioDeviceNames];
                ConfigUpdated?.Invoke(this);
            }

            if (_activeAudioDevice != activeAudioDevice)
            {
                _activeAudioDevice = activeAudioDevice;
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
