using HomeAssistantDiscoveryNet;

namespace HADesktopAgent.Core.Entity
{
    public static class IHaEntityExtensions
    {
        public static MqttDiscoveryConfig GetConfig(this IHaEntity entity, string appPrefix, string availabilityTopic, string deviceName, string deviceId)
        {
            if (!MqttDiscoveryConfigParser.OfficalDiscoveryConfigTypes.TryGetValue(entity.EntityType, out Type? type))
            {
                throw new KeyNotFoundException($"Entity type '{entity.EntityType}' is not a valid");
            }

            if (Activator.CreateInstance(type) is not MqttDiscoveryConfig config)
            {
                throw new InvalidOperationException("Error instantiating MqttDiscoveryConfig");
            }

            SetProperty(type, config, "Name", entity.PrettyName);
            SetProperty(type, config, "UniqueId", entity.UniqueId);
            SetProperty(type, config, "Icon", entity.Icon);
            SetProperty(type, config, "AvailabilityTopic", availabilityTopic);
            SetProperty(type, config, "Device", new MqttDiscoveryDevice
            {
                Name = deviceName,
                Identifiers = [deviceId],
            });

            if (entity is IHaStatefulEntity statefulEntity)
            {
                SetProperty(type, config, "StateTopic", statefulEntity.GetStateTopic(appPrefix));
                SetProperty(type, config, "Optimistic", statefulEntity.Optimistic);
            }
            if (entity is IHaCommandableEntity commandableEntity)
            {
                SetProperty(type, config, "CommandTopic", commandableEntity.GetCommandTopic(appPrefix));
            }
            if (entity is IHaSelectableEntity selectableEntity)
            {
                SetProperty(type, config, "Options", selectableEntity.Options.ToList());
            }
            if (entity is IHaClassifiableDevice classifiableDeviceEntity)
            {
                SetProperty(type, config, "DeviceClass", classifiableDeviceEntity.DeviceClass);
            }

            return config;
        }
        private static void SetProperty(Type type, object instance, string propertyName, object? value)
        {
            var property = type.GetProperty(propertyName)
                ?? throw new InvalidOperationException($"Property '{propertyName}' not found on type '{type.Name}'");
            property.SetValue(instance, value);
        }
    }
}
