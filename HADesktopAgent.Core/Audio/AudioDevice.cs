namespace HADesktopAgent.Core.Audio
{
    public record AudioDevice
    {
        public required string Id { get; init; }
        public required string FriendlyName { get; init; }
        public required string UserFriendlyName { get; init; }
        public required string DeviceName { get; init; }
        public required bool IsActive { get; init; }
    }
}
