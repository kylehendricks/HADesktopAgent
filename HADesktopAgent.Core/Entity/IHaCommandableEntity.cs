namespace HADesktopAgent.Core.Entity
{
    public interface IHaCommandableEntity : IHaEntity
    {
        string GetCommandTopic(string appPrefix, string deviceId) => $"{appPrefix}/{deviceId}/{Name}/command";

        void HandleCommand(string command);
    }
}
