namespace HADesktopAgent.Core.Entity
{
    public interface IHaSelectableEntity : IHaEntity
    {
        SortedSet<string> Options { get; }
    }
}
