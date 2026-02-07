using HAWindowsAgent.Entity;

namespace HAWindowsAgent.Sleep.Entity
{
    public class SleepButton : IHaCommandableEntity
    {
        public string Name => "sleep_computer";
        public string PrettyName => "Sleep";
        public string UniqueId => Name;
        public string Icon => "mdi:power-sleep";
        public string EntityType => "button";

        public event IHaEntity.ConfigUpdatedHandler? ConfigUpdated { add { } remove { } }

        public void HandleCommand(string command)
        {
            PowerControl.Sleep();
        }
    }
}
