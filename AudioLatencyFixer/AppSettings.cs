namespace AudioLatencyFixer
{
    public class AppSettings
    {
        public bool LatencyFixEnabled { get; set; } = false;

        public bool BoostProcessPriority { get; set; } = false;
        public bool BoostThreadPriority { get; set; } = false;
        public bool DisableAudioDucking { get; set; } = false;
        public bool EnableAdvancedMode { get; set; } = false;
    }
}