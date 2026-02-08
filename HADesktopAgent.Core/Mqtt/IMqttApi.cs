namespace HADesktopAgent.Core.Mqtt
{
    public interface IMqttApi
    {
        string Name { get; }
        void HandleCommand(string payload);
    }
}
