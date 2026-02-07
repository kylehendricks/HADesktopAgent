using HomeAssistantDiscoveryNet;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HAWindowsAgent
{
    public static class MqttDiscoveryConfigExtensions
    {
        public static string ToJson<T>(this T config, JsonSerializerContext? ctx = null) where T : MqttDiscoveryConfig
        {
            ctx ??= MqttDiscoveryJsonContext.Default;
            var jsonTypeInfo = ctx.GetTypeInfo(config.GetType()) ?? throw new InvalidOperationException("The JsonTypeInfo for " + config.GetType().FullName + " was not found in the provided JsonSerializerContext. If you have a custom Discovery Document you might need to provide your own JsonSerializerContext");
            return JsonSerializer.Serialize(config, jsonTypeInfo);
        }

        public static string ToJson(this MqttDeviceDiscoveryConfig deviceConfig, JsonSerializerContext? ctx = null)
        {
            ctx ??= MqttDiscoveryJsonContext.Default;
            var jsonTypeInfo = ctx.GetTypeInfo(deviceConfig.GetType()) ?? throw new InvalidOperationException("The JsonTypeInfo for " + deviceConfig.GetType().FullName + " was not found in the provided JsonSerializerContext. If you have a custom Discovery Document you might need to provide your own JsonSerializerContext");
            return JsonSerializer.Serialize(deviceConfig, jsonTypeInfo);
        }
    }
}
