namespace HADesktopAgent.Core.Entity
{
    public interface IHaStatefulEntity : IHaEntity
    {
        public const string ON_STATE = "ON";
        public const string OFF_STATE = "OFF";
        delegate void StateUpdatedHandler(IHaStatefulEntity entity);

        bool Optimistic => true;

        public string? State { get; }

        event StateUpdatedHandler? StateUpdated;
        string GetStateTopic(string appPrefix) => $"{appPrefix}/{Name}/state";
    }
}
