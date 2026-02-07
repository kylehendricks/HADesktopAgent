using HADesktopAgent.Core.Sleep;
using HADesktopAgent.Linux.DBus;
using Tmds.DBus;

namespace HADesktopAgent.Linux.Sleep
{
    public class SystemdSleepControl : ISleepControl
    {
        public void Sleep()
        {
            var logindManager = Connection.System.CreateProxy<ILogindManager>(
                "org.freedesktop.login1",
                "/org/freedesktop/login1");

            logindManager.SuspendAsync(false).GetAwaiter().GetResult();
        }
    }
}
