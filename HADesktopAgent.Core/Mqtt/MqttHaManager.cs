using HADesktopAgent.Core.Entity;
using Microsoft.Extensions.Logging;

namespace HADesktopAgent.Core.Mqtt
{

    public class MqttHaManager : IDisposable, IHaEntityHost
    {
        private readonly ILogger<MqttHaManager> _logger;
        private readonly MqttManager _mqttManager;
        private readonly string _deviceId;
        private readonly string _deviceName;
        private readonly string _haPrefix;
        private readonly string _appPrefix;
        private readonly List<IHaEntity> _entities = [];
        private readonly Dictionary<string, IHaCommandableEntity> _topicToCommandableEntities = [];
        private readonly Dictionary<string, IMqttApi> _topicToApis = [];

        // Entities register/unregister on app threads while the on-connect bulk
        // publish and inbound message routing run on background threads.
        private readonly object _collectionLock = new();

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
            lock (_collectionLock)
            {
                _entities.Add(entity);
            }

            entity.ConfigUpdated += HandleEntityConfigUpdated;
            if (_mqttManager.IsConnected)
            {
                await PublishEntityConfig(entity);
            }

            if (entity is IHaCommandableEntity commandableEntity)
            {
                var commandTopic = commandableEntity.GetCommandTopic(_appPrefix, _deviceId);
                lock (_collectionLock)
                {
                    if (_topicToCommandableEntities.ContainsKey(commandTopic))
                    {
                        throw new InvalidOperationException($"Command Topic '{commandTopic}' already subscribed to.");
                    }

                    _topicToCommandableEntities[commandTopic] = commandableEntity;
                }

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

        public async Task RegisterApi(IMqttApi api)
        {
            var commandTopic = $"{_appPrefix}/{_deviceId}/{api.Name}/command";
            lock (_collectionLock)
            {
                if (_topicToApis.ContainsKey(commandTopic))
                {
                    throw new InvalidOperationException($"API command topic '{commandTopic}' already subscribed to.");
                }

                _topicToApis[commandTopic] = api;
            }

            if (_mqttManager.IsConnected)
            {
                await SubscribeCommandTopic(commandTopic);
            }
        }

        public async Task UnregisterApi(IMqttApi api)
        {
            var commandTopic = $"{_appPrefix}/{_deviceId}/{api.Name}/command";
            bool removed;
            lock (_collectionLock)
            {
                removed = _topicToApis.Remove(commandTopic);
            }

            if (removed && _mqttManager.IsConnected)
            {
                await _mqttManager.UnsubscribeAsync(commandTopic);
            }
        }

        public async Task UnregisterEntity(IHaEntity entity)
        {
            bool removed;
            lock (_collectionLock)
            {
                removed = _entities.Remove(entity);
            }

            if (!removed)
                return;

            // Publish empty config to remove from HA discovery
            try
            {
                await _mqttManager.PublishAsync(entity.GetConfigTopic(_haPrefix, _deviceId), "", true);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Failed to publish empty config for '{Name}'", entity.Name);
            }

            if (entity is IHaCommandableEntity commandableEntity)
            {
                var commandTopic = commandableEntity.GetCommandTopic(_appPrefix, _deviceId);
                await Unsubscribe(commandTopic);
            }

            if (entity is IHaStatefulEntity statefulEntity)
            {
                statefulEntity.StateUpdated -= HandleEntityStateUpdated;
            }

            entity.ConfigUpdated -= HandleEntityConfigUpdated;
        }

        private async Task Unsubscribe(string topic)
        {
            lock (_collectionLock)
            {
                _topicToCommandableEntities.Remove(topic);
            }

            // A failed broker unsubscribe must not abort the caller's cleanup;
            // the subscription is gone server-side once the connection drops anyway.
            try
            {
                if (_mqttManager.IsConnected)
                {
                    await _mqttManager.UnsubscribeAsync(topic);
                }
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Failed to unsubscribe from topic '{Topic}'", topic);
            }
        }

        private async Task PublishEntityConfig(IHaEntity entity)
        {
            try
            {
                var config = entity.GetConfig(_appPrefix, _mqttManager.StatusTopic, _deviceName, _deviceId);
                await _mqttManager.PublishAsync(entity.GetConfigTopic(_haPrefix, _deviceId), config.ToJson(), true);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Failed to publish configuration for '{Name}'", entity.Name);
            }
        }

        private async Task PublishAllConfigs()
        {
            List<IHaEntity> entities;
            lock (_collectionLock)
            {
                entities = [.. _entities];
            }

            await Task.WhenAll(entities.Select(e => PublishEntityConfig(e)));
        }
        private async Task PublishAllStates()
        {
            List<IHaStatefulEntity> entities;
            lock (_collectionLock)
            {
                entities = [.. _entities.OfType<IHaStatefulEntity>()];
            }

            await Task.WhenAll(entities.Select(e => PublishEntityState(e)));
        }
        private async Task SubscribeAllCommandTopics()
        {
            List<string> commandTopics;
            lock (_collectionLock)
            {
                commandTopics = [.. _topicToCommandableEntities.Keys.Concat(_topicToApis.Keys)];
            }

            await Task.WhenAll(commandTopics.Select(commandTopic => SubscribeCommandTopic(commandTopic)));
        }

        private async Task PublishEntityState(IHaStatefulEntity entity)
        {
            try
            {
                await _mqttManager.PublishAsync(entity.GetStateTopic(_appPrefix, _deviceId), entity.State ?? "", true);
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
            _ = RunLogged(PublishAllConfigs, "publish all entity configs");
            _ = RunLogged(PublishAllStates, "publish all entity states");
            _ = RunLogged(SubscribeAllCommandTopics, "subscribe all command topics");
        }

        /// <summary>
        /// Runs a fire-and-forget task, logging any failure instead of silently
        /// discarding it with the task.
        /// </summary>
        private async Task RunLogged(Func<Task> action, string description)
        {
            try
            {
                await action();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to {Description}", description);
            }
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
            IHaCommandableEntity? commandableEntity;
            IMqttApi? api = null;
            lock (_collectionLock)
            {
                if (!_topicToCommandableEntities.TryGetValue(topic, out commandableEntity))
                {
                    _topicToApis.TryGetValue(topic, out api);
                }
            }

            if (commandableEntity != null)
            {
                commandableEntity.HandleCommand(payload);
            }
            else if (api != null)
            {
                api.HandleCommand(payload);
            }
        }

        public async void Dispose()
        {
            // async void: nothing observes exceptions thrown here, and an unhandled
            // one would crash the process during shutdown.
            try
            {
                List<IHaEntity> entities;
                List<string> apiTopics;
                lock (_collectionLock)
                {
                    entities = [.. _entities];
                    apiTopics = [.. _topicToApis.Keys];
                    _topicToApis.Clear();
                }

                foreach (var entity in entities)
                {
                    if (entity is IHaCommandableEntity commandableEntity)
                    {
                        await Unsubscribe(commandableEntity.GetCommandTopic(_appPrefix, _deviceId));
                    }
                    if (entity is IHaStatefulEntity statefulEntity)
                    {
                        statefulEntity.StateUpdated -= HandleEntityStateUpdated;
                    }

                    entity.ConfigUpdated -= HandleEntityConfigUpdated;
                }

                foreach (var topic in apiTopics)
                {
                    await Unsubscribe(topic);
                }
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Error during MQTT HA manager cleanup");
            }
            finally
            {
                _mqttManager.MqttMessage -= HandleMqttMessage;
                _mqttManager.MqttConnected -= HandleMqttConnected;
            }
        }
    }
}
