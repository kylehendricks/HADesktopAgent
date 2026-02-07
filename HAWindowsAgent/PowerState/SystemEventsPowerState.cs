using Microsoft.Win32;

namespace HAWindowsAgent.PowerState
{
    public sealed class SystemEventsPowerState : IPowerState, IDisposable
    {
        public event IPowerState.OnSuspendHandler? OnSuspend;
        public event IPowerState.OnResumeHandler? OnResume;

        public SystemEventsPowerState()
        {
            SystemEvents.PowerModeChanged += PowerModeChanged;
        }

        private void PowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            switch (e.Mode)
            {
                case PowerModes.Resume:
                    OnResume?.Invoke();
                    break;
                case PowerModes.Suspend:
                    OnSuspend?.Invoke();
                    break;
            }
        }

        public void Dispose()
        {
            SystemEvents.PowerModeChanged -= PowerModeChanged;
        }
    }
}
