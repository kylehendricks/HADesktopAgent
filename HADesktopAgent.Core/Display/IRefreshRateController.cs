namespace HADesktopAgent.Core.Display
{
    /// <summary>
    /// Controls monitor refresh rates. Rates are queried at the monitor's current
    /// resolution; changing resolution may change which rates are available.
    /// </summary>
    public interface IRefreshRateController
    {
        /// <summary>
        /// Gets the refresh rates (in Hz) supported at the monitor's current resolution.
        /// Empty if the monitor is not active or cannot be resolved.
        /// </summary>
        List<int> GetAvailableRefreshRates(string monitorName);

        /// <summary>
        /// Gets the monitor's current refresh rate in Hz, or null if unavailable.
        /// </summary>
        int? GetCurrentRefreshRate(string monitorName);

        /// <summary>
        /// Sets the monitor's refresh rate, keeping the current resolution.
        /// </summary>
        bool SetRefreshRate(string monitorName, int refreshRate);
    }
}
