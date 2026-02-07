using HADesktopAgent.Core.Entity;
using Microsoft.Extensions.Logging;

namespace HADesktopAgent.Core.Mqtt
{

    public class MqttHaManager : IDisposable
    {
        private readonly ILogger<MqttHaManager> _logger;
        private readonly MqttManager _mqttManager;
        private readonly string _deviceId;
        private readonly string _deviceName;
        private readonly string _haPrefix;
        private readonly string _appPrefix;
        private readonly List<IHaEntity> _entities = [];
        private readonly Dictionary<string, IHaCommandableEntity> _topicToCommandableEntities = [];

        public MqttHaManager(ILogger<MqttHaManager> logger, MqttManager mqttManager, string haPrefix, string appPrefix, string deviceId, string deviceName)
        {
            _logger = logger;
            _mqttManager = mqttManager;
            _deviceId = deviceId;
            _deviceName = deviceName;
            _haPrefix = haPrefix;
            _appPrefix = appPrefix;

            _mqttManager.MqttConnected += HandleMqttConnected;
            _mqttManager.MqttMessage += HandleMqttMessage;
        }

        public async Task RegisterEntity(IHaEntity entity)
        {
            _entities.Add(entity);

            entity.ConfigUpdated += HandleEntityConfigUpdated;
            if (_mqttManager.IsConnected)
            {
                await PublishEntityConfig(entity);
            }

            if (entity is IHaCommandableEntity commandableEntity)
            {
                var commandTopic = commandableEntity.GetCommandTopic(_appPrefix);
                if (_topicToCommandableEntities.ContainsKey(commandTopic))
                {
                    throw new InvalidOperationException($"Command Topic '{commandTopic}' already subscribed to.");
                }

                _topicToCommandableEntities[commandTopic] = commandableEntity;

                if (_mqttManager.IsConnected)
                {
                    await SubscribeCommandTopic(commandTopic);
                }
            }

            if (entity is IHaStatefulEntity statefulEntity)
            {
                statefulEntity.StateUpdated += HandleEntityStateUpdated;

                if (_mqttManager.IsConnected)
                {
                    await PublishEntityState(statefulEntity);
                }
            }
        }

        private async Task Unsubscribe(string topic)
        {
            _topicToCommandableEntities.Remove(topic);
            await _mqttManager.UnsubscribeAsync(topic);
        }

        private async Task PublishEntityConfig(IHaEntity entity)
        {
            try
            {
                var config = entity.GetConfig(_appPrefix, _mqttManager.StatusTopic, _deviceName, _deviceId);
                await _mqttManager.PublishAsync(entity.GetConfigTopic(_haPrefix), config.ToJson(), true);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Failed to publish configuration for '{Name}'", entity.Name);
            }
        }

        private async Task PublishAllConfigs()
        {
            await Task.WhenAll(_entities.Select(e => PublishEntityConfig(e)));
        }
        private async Task PublishAllStates()
        {
            await Task.WhenAll(
                _entities
                    .OfType<IHaStatefulEntity>()
                    .Select(e => PublishEntityState(e)));
        }
        private async Task SubscribeAllCommandTopics()
        {
            await Task.WhenAll(
                _topicToCommandableEntities.Keys
                    .Select(commandTopic => SubscribeCommandTopic(commandTopic)));
        }

        private async Task PublishEntityState(IHaStatefulEntity entity)
        {
            try
            {
                await _mqttManager.PublishAsync(entity.GetStateTopic(_appPrefix), entity.State ?? "", true);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to publish state for entity {Name}", entity.Name);
            }
        }

        private async Task SubscribeCommandTopic(string commandTopic)
        {
            try
            {
                await _mqttManager.SubscribeAsync(commandTopic);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to subscribe to command topic '{commandTopic}'", commandTopic);
            }
        }

        private void HandleMqttConnected()
        {
            _ = PublishAllConfigs();
            _ = PublishAllStates();
            _ = SubscribeAllCommandTopics();
        }

        private void HandleEntityStateUpdated(IHaStatefulEntity entity)
        {
            _logger.LogDebug("{Device} State Updated: {State}", entity.Name, entity.State);

            if (!_mqttManager.IsConnected)
            {
                return;
            }

            _ = PublishEntityState(entity);
        }
        private void HandleEntityConfigUpdated(IHaEntity entity)
        {
            if (!_mqttManager.IsConnected)
            {
                return;
            }

            _ = PublishEntityConfig(entity);
        }

        private void HandleMqttMessage(string topic, string payload)
        {
            if (_topicToCommandableEntities.TryGetValue(topic, out var commandableEntity))
            {
                commandableEntity.HandleCommand(payload);
            }
        }

        public async void Dispose()
        {
            foreach (var entity in _entities)
            {
                if (entity is IHaCommandableEntity commandableEntity)
                {
                    await Unsubscribe(commandableEntity.GetCommandTopic(_appPrefix));
                }
                if (entity is IHaStatefulEntity statefulEntity)
                {
                    statefulEntity.StateUpdated -= HandleEntityStateUpdated;
                }

                entity.ConfigUpdated -= HandleEntityConfigUpdated;
            }

            _mqttManager.MqttMessage -= HandleMqttMessage;
            _mqttManager.MqttConnected -= HandleMqttConnected;
        }
    }
}
