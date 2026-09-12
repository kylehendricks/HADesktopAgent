using System.ComponentModel.DataAnnotations;

namespace HADesktopAgent.Core.Mqtt
{
    public class MqttConfiguration
    {
        [Required(ErrorMessage = "MQTT Host is required")]
        public string Host { get; set; } = "127.0.0.1";

        public string Username { get; set; } = string.Empty;

        public string Password { get; set; } = string.Empty;

        /// <summary>
        /// Path to a file whose contents are the password, read at startup and used in place
        /// of <see cref="Password"/>. Lets a generated config.json carry the path instead of
        /// the secret itself. A single trailing newline is stripped.
        /// </summary>
        public string? PasswordFile { get; set; }

        [Required(ErrorMessage = "MQTT Status topic is required")]
        public string StatusTopic { get; set; } = "ha_desktop_agent/status";

        [Required(ErrorMessage = "MQTT Discovery prefix is required")]
        public string DiscoveryPrefix { get; set; } = "homeassistant";
    }
}
