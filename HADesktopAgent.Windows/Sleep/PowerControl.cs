using System.Runtime.InteropServices;

namespace HADesktopAgent.Windows.Sleep
{
    public partial class PowerControl
    {
        [LibraryImport("PowrProf.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetSuspendState(
            [MarshalAs(UnmanagedType.Bool)] bool hibernate,
            [MarshalAs(UnmanagedType.Bool)] bool forceCritical,
            [MarshalAs(UnmanagedType.Bool)] bool disableWakeEvent);

        public static void Sleep()
        {
            SetSuspendState(false, false, false);
        }
    }
}
