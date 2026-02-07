using Tmds.DBus;

namespace HADesktopAgent.Linux.DBus
{
    [DBusInterface("org.freedesktop.login1.Manager")]
    public interface ILogindManager : IDBusObject
    {
        Task SuspendAsync(bool interactive);
        Task<IDisposable> WatchPrepareForSleepAsync(Action<bool> handler, Action<Exception>? onError = null);
    }
}
