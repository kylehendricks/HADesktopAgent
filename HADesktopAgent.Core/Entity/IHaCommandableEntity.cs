namespace HADesktopAgent.Core.Entity
{
    public interface IHaCommandableEntity : IHaEntity
    {
        string GetCommandTopic(string appPrefix) => $"{appPrefix}/{Name}/command";

        void HandleCommand(string command);
    }
}
