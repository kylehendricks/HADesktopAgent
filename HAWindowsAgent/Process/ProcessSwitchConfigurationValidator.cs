using Microsoft.Extensions.Options;

namespace HAWindowsAgent.Process
{
    public class ProcessSwitchConfigurationValidator : IValidateOptions<List<ProcessSwitchConfiguration>>
    {
        public ValidateOptionsResult Validate(string? name, List<ProcessSwitchConfiguration> options)
        {
            var errors = new List<string>();

            for (int i = 0; i < options.Count; i++)
            {
                var config = options[i];

                // Validate Name is not whitespace
                if (string.IsNullOrWhiteSpace(config.Name))
                {
                    errors.Add($"ProcessSwitch[{i}]: Name cannot be empty or whitespace");
                }

                // Validate PrettyName is not whitespace
                if (string.IsNullOrWhiteSpace(config.PrettyName))
                {
                    errors.Add($"ProcessSwitch[{i}]: PrettyName cannot be empty or whitespace");
                }

                // Validate ApplicationPath is not whitespace
                if (string.IsNullOrWhiteSpace(config.ApplicationPath))
                {
                    errors.Add($"ProcessSwitch[{i}]: ApplicationPath cannot be empty or whitespace");
                }
            }

            // Check for duplicate Names
            var duplicateNames = options
                .GroupBy(x => x.Name)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicateNames.Any())
            {
                errors.Add($"Duplicate ProcessSwitch Names found: {string.Join(", ", duplicateNames)}");
            }

            return errors.Any()
                ? ValidateOptionsResult.Fail(errors)
                : ValidateOptionsResult.Success;
        }
    }
}
