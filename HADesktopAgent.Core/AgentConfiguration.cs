using System.ComponentModel.DataAnnotations;

namespace HADesktopAgent.Core
{
    public class AgentConfiguration
    {
        [Required(ErrorMessage = "Agent DeviceId is required")]
        public string DeviceId { get; set; } = "ha_agent";

        [Required(ErrorMessage = "Agent DeviceName is required")]
        public string DeviceName { get; set; } = "HA Agent";
    }
}
