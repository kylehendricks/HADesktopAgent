using HADesktopAgent.Core.Entity;

namespace HADesktopAgent.Core.Sleep.Entity
{
    public class SleepButton : IHaCommandableEntity
    {
        public string Name => "sleep_computer";
        public string PrettyName => "Sleep";
        public string UniqueId => Name;
        public string Icon => "mdi:power-sleep";
        public string EntityType => "button";

        private readonly ISleepControl _sleepControl;

        public event IHaEntity.ConfigUpdatedHandler? ConfigUpdated { add { } remove { } }

        public SleepButton(ISleepControl sleepControl)
        {
            _sleepControl = sleepControl;
        }

        public void HandleCommand(string command)
        {
            _sleepControl.Sleep();
        }
    }
}
