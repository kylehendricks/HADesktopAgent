using HADesktopAgent.Core.Mqtt;

namespace HADesktopAgent.Core.Entity
{
    /// <summary>
    /// Hosts entities and APIs built by <see cref="AgentEntityBuilder"/>.
    /// </summary>
    /// <remarks>
    /// Implemented by <see cref="MqttHaManager"/> for normal operation, and by
    /// <c>ConsoleHaHost</c> for the offline dev console. Entities themselves have no
    /// knowledge of MQTT — they raise <see cref="IHaStatefulEntity.StateUpdated"/> and
    /// accept <see cref="IHaCommandableEntity.HandleCommand"/> — so the whole agent can
    /// run against either host.
    /// </remarks>
    public interface IHaEntityHost
    {
        Task RegisterEntity(IHaEntity entity);

        Task UnregisterEntity(IHaEntity entity);

        Task RegisterApi(IMqttApi api);

        Task UnregisterApi(IMqttApi api);
    }
}
