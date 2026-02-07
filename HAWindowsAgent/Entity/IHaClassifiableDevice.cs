namespace HAWindowsAgent.Entity
{
    public interface IHaClassifiableDevice : IHaEntity
    {
        string DeviceClass { get; }
    }
}
