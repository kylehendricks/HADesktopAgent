using HADesktopAgent.Core.PowerState;

namespace HADesktopAgent.Linux.PowerState
{
    public class NoOpPowerState : IPowerState
    {
        public event IPowerState.OnSuspendHandler? OnSuspend { add { } remove { } }
        public event IPowerState.OnResumeHandler? OnResume { add { } remove { } }
    }
}
