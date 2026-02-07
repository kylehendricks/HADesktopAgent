namespace HADesktopAgent.Core.Audio
{
    public interface IAudioManager
    {
        List<AudioDevice> GetAudioDevices();
        void SetActiveDevice(string deviceId);
        event EventHandler? DeviceChanged;
    }
}
