namespace HAWindowsAgent.Entity
{
    public interface IHaEntity
    {
        delegate void ConfigUpdatedHandler(IHaEntity entity);

        event ConfigUpdatedHandler? ConfigUpdated;
        string GetConfigTopic(string haPrefix) => $"{haPrefix}/{EntityType}/{Name}/config";
        string Name { get; }
        string PrettyName { get; }
        string UniqueId { get; }
        string Icon { get; }
        string EntityType { get; }
    }
}
