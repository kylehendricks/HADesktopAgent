using HAWindowsAgent.Entity;

namespace HAWindowsAgent.Audio.Entity
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

        private readonly AudioDeviceManager _audioDeviceManager;
        private readonly ILogger<AudioSelect> _logger;

        private SortedSet<string> _audioDeviceNames = [];
        private string? _activeAudioDevice;

        public event IHaStatefulEntity.StateUpdatedHandler? StateUpdated;
        public event IHaEntity.ConfigUpdatedHandler? ConfigUpdated;

        public AudioSelect(ILogger<AudioSelect> logger, AudioDeviceManager audioDeviceManager)
        {
            _logger = logger;
            _audioDeviceManager = audioDeviceManager;
            UpdateAudioDevices();

            _audioDeviceManager.DeviceChanged += AudioDevicesChanged;
        }

        public void HandleCommand(string command)
        {
            var audioDevice = AudioDeviceManager.GetAudioDevices().Find(a => a.UserFriendlyName == command);

            if (audioDevice == null)
            {
                Console.WriteLine($"Audio device '{command}' not found");
                return;
            }

            AudioDeviceManager.SetActiveDevice(audioDevice.Id);
        }

        private void UpdateAudioDevices()
        {
            var audioDevices = AudioDeviceManager.GetAudioDevices();
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
            _audioDeviceManager.DeviceChanged -= AudioDevicesChanged;
        }
    }
}
