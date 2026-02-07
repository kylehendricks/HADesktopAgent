namespace HADesktopAgent.Core.Entity
{
    public interface IHaEntity
    {
        delegate void ConfigUpdatedHandler(IHaEntity entity);

        event ConfigUpdatedHandler? ConfigUpdated;
        string GetConfigTopic(string haPrefix, string deviceId) => $"{haPrefix}/{EntityType}/{deviceId}_{Name}/config";
        string Name { get; }
        string PrettyName { get; }
        string UniqueId { get; }
        string Icon { get; }
        string EntityType { get; }
    }
}
