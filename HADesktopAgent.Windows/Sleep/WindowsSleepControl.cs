using HADesktopAgent.Core.Sleep;

namespace HADesktopAgent.Windows.Sleep
{
    public class WindowsSleepControl : ISleepControl
    {
        public void Sleep()
        {
            PowerControl.Sleep();
        }
    }
}
