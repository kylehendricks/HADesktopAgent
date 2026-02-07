namespace HADesktopAgent.Core.Entity
{
    public interface IHaClassifiableDevice : IHaEntity
    {
        string DeviceClass { get; }
    }
}
