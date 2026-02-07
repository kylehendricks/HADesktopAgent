using System.Diagnostics;

namespace HADesktopAgent.Core.Process
{
    public static class ApplicationLauncher
    {
        /// <summary>
        /// Launches an application from the specified executable path
        /// </summary>
        /// <param name="exePath">Full path to the executable file</param>
        /// <param name="arguments">Optional command-line arguments</param>
        /// <returns>The Process object for the started application, or null if it failed to start</returns>
        public static System.Diagnostics.Process? Launch(string exePath, string? arguments = null)
        {
            if (string.IsNullOrWhiteSpace(exePath))
            {
                throw new ArgumentException("Executable path cannot be null or empty", nameof(exePath));
            }

            if (!File.Exists(exePath))
            {
                throw new FileNotFoundException($"Executable not found: {exePath}", exePath);
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true
                };

                if (!string.IsNullOrWhiteSpace(arguments))
                {
                    startInfo.Arguments = arguments;
                }

                return System.Diagnostics.Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to launch application: {exePath}", ex);
            }
        }

        /// <summary>
        /// Kills all running instances of an application
        /// </summary>
        /// <param name="exePath">Full path to the executable file</param>
        /// <returns>True if all instances were killed, false if any are still running</returns>
        public static bool Exit(string exePath)
        {
            if (string.IsNullOrWhiteSpace(exePath))
            {
                throw new ArgumentException("Executable path cannot be null or empty", nameof(exePath));
            }

            var processName = Path.GetFileNameWithoutExtension(exePath);
            var processes = System.Diagnostics.Process.GetProcessesByName(processName);

            if (processes.Length == 0)
            {
                // Process is not running
                return true;
            }

            // Kill all instances
            foreach (var process in processes)
            {
                try
                {
                    process.Kill();
                    process.WaitForExit();
                }
                catch
                {
                    // Ignore exceptions for individual processes
                }
                finally
                {
                    process.Dispose();
                }
            }

            // Verify all instances are gone
            var remainingProcesses = System.Diagnostics.Process.GetProcessesByName(processName);
            return remainingProcesses.Length == 0;
        }
    }
}
