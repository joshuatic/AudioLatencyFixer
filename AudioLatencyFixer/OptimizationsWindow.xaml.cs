using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;

namespace AudioLatencyFixer
{
    public partial class OptimizationsWindow : Window
    {
        private AppSettings settings;

        public OptimizationsWindow(AppSettings settings)
        {
            InitializeComponent();
            this.settings = settings;

            // Load saved values into UI
            PriorityBoostCheck.IsChecked = settings.BoostProcessPriority;
            ThreadBoostCheck.IsChecked = settings.BoostThreadPriority;
            DuckingCheck.IsChecked = settings.DisableAudioDucking;
            AdvancedModeCheck.IsChecked = settings.EnableAdvancedMode;
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // SAVE SETTINGS FIRST
                settings.BoostProcessPriority = PriorityBoostCheck.IsChecked == true;
                settings.BoostThreadPriority = ThreadBoostCheck.IsChecked == true;
                settings.DisableAudioDucking = DuckingCheck.IsChecked == true;
                settings.EnableAdvancedMode = AdvancedModeCheck.IsChecked == true;

                // APPLY LOGIC
                if (settings.BoostProcessPriority)
                {
                    try
                    {
                        Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
                    }
                    catch { }
                }

                if (settings.BoostThreadPriority)
                {
                    try
                    {
                        Thread.CurrentThread.Priority = ThreadPriority.Highest;
                    }
                    catch { }
                }

                if (settings.DisableAudioDucking)
                {
                    using (var key = Registry.CurrentUser.CreateSubKey(
                               @"Software\Microsoft\Multimedia\Audio"))
                    {
                        key?.SetValue("UserDuckingPreference", 3);
                    }
                }

                if (settings.EnableAdvancedMode)
                {
                    MessageBox.Show("Advanced mode coming soon!");
                }

                MessageBox.Show("Optimizations applied.");
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }
    }
}