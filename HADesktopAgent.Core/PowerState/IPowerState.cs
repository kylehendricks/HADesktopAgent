namespace HADesktopAgent.Core.PowerState
{
    public interface IPowerState
    {
        delegate void OnSuspendHandler();
        delegate void OnResumeHandler();

        event OnSuspendHandler? OnSuspend;
        event OnResumeHandler? OnResume;
    }
}
