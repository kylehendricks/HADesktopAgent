using System.ComponentModel.DataAnnotations;

namespace HADesktopAgent.Core.Process
{
    public class ProcessSwitchConfiguration
    {
        [Required(ErrorMessage = "ProcessSwitch Name is required")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "ProcessSwitch PrettyName is required")]
        public string PrettyName { get; set; } = string.Empty;

        public string Icon { get; set; } = "mdi:application";

        [Required(ErrorMessage = "ProcessSwitch ApplicationPath is required")]
        public string ApplicationPath { get; set; } = string.Empty;

        public string? StartArgument { get; set; }

        public string? StopArgument { get; set; }
    }
}
