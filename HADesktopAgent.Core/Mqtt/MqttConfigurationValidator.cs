using Microsoft.Extensions.Options;

namespace HADesktopAgent.Core.Mqtt
{
    public class MqttConfigurationValidator : IValidateOptions<MqttConfiguration>
    {
        public ValidateOptionsResult Validate(string? name, MqttConfiguration options)
        {
            var errors = new List<string>();

            // Validate Host is not just whitespace
            if (string.IsNullOrWhiteSpace(options.Host))
            {
                errors.Add("MQTT Host cannot be empty or whitespace");
            }

            // Validate username/password pairing - both must be set or both must be empty
            bool hasUsername = !string.IsNullOrEmpty(options.Username);
            bool hasPassword = !string.IsNullOrEmpty(options.Password);

            if (hasUsername != hasPassword)
            {
                errors.Add("MQTT Username and Password must both be set or both be empty");
            }

            // Validate StatusTopic is not just whitespace
            if (string.IsNullOrWhiteSpace(options.StatusTopic))
            {
                errors.Add("MQTT Status topic cannot be empty or whitespace");
            }

            return errors.Any()
                ? ValidateOptionsResult.Fail(errors)
                : ValidateOptionsResult.Success;
        }
    }
}
