using Microsoft.Extensions.Options;

namespace HADesktopAgent.Core.Profiles
{
    public class ProfileConfigurationValidator : IValidateOptions<Dictionary<string, ProfileConfiguration>>
    {
        public ValidateOptionsResult Validate(string? name, Dictionary<string, ProfileConfiguration> options)
        {
            var errors = new List<string>();

            foreach (var (profileName, profile) in options)
            {
                if (string.IsNullOrWhiteSpace(profileName))
                {
                    errors.Add("Profiles: a profile name cannot be empty or whitespace");
                }

                // An MQTT select treats a "None" state payload as "no option selected", so a
                // profile with that name could never be shown as active.
                if (profileName.Equals(ProfileSelect.NoProfileState, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"Profiles: '{profileName}' is reserved by Home Assistant");
                }

                if (profile.Monitors.Count == 0 || profile.Monitors.Any(string.IsNullOrWhiteSpace))
                {
                    errors.Add($"Profiles.{profileName}: Monitors must list at least one monitor, with no empty names");
                }

                foreach (var (monitor, rate) in profile.RefreshRates)
                {
                    if (!profile.Monitors.Contains(monitor))
                    {
                        errors.Add($"Profiles.{profileName}: RefreshRates names '{monitor}', which is not in Monitors");
                    }

                    if (rate <= 0)
                    {
                        errors.Add($"Profiles.{profileName}: RefreshRates.{monitor} must be a positive rate in Hz");
                    }
                }
            }

            return errors.Count > 0
                ? ValidateOptionsResult.Fail(errors)
                : ValidateOptionsResult.Success;
        }
    }
}
