namespace HAWindowsAgent.Entity
{
    public interface IHaSelectableEntity : IHaEntity
    {
        SortedSet<string> Options { get; }
    }
}
