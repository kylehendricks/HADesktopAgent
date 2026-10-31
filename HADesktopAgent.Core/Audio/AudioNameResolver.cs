namespace HADesktopAgent.Core.Audio
{
    /// <summary>
    /// Applies <see cref="NameMappingConfiguration.AudioDevices"/> to the devices the OS
    /// reports. Shared by the audio select and the profile select.
    /// </summary>
    public sealed class AudioNameResolver
    {
        private readonly IReadOnlyDictionary<string, string> _mappings;

        public AudioNameResolver(IReadOnlyDictionary<string, string>? mappings)
        {
            _mappings = mappings ?? new Dictionary<string, string>();
        }

        /// <summary>
        /// Checks the device's UserFriendlyName, FriendlyName, and DeviceName against the
        /// mapping keys, in that order, falling back to UserFriendlyName.
        /// </summary>
        public string Resolve(AudioDevice device)
        {
            if (_mappings.TryGetValue(device.UserFriendlyName, out var mappedByUserFriendly))
                return mappedByUserFriendly;

            if (_mappings.TryGetValue(device.FriendlyName, out var mappedByFriendly))
                return mappedByFriendly;

            if (_mappings.TryGetValue(device.DeviceName, out var mappedByDevice))
                return mappedByDevice;

            return device.UserFriendlyName;
        }
    }
}
