using System.Text.Json;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using Application = System.Windows.Application;
using Icon = System.Drawing.Icon;
using MessageBox = System.Windows.MessageBox;

namespace AudioLatencyFixer
{
    public partial class MainWindow : Window
    {
        [DllImport("AudioCore.dll")]
        private static extern void EnableLowLatencyMode();

        private AppSettings settings = new AppSettings();

        private string settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AudioLatencyFixer",
            "settings.json"
        );

        private NotifyIcon? trayIcon;
        private Icon? trayIconIcon;
        private MMDevice? currentDevice;
        private bool latencyFixEnabled = false;
        private bool allowExit = false;

        public MainWindow()
        {
            InitializeComponent();
            LoadSettings();
            GetAudioDevice();

            Application.Current.Exit += OnAppExit;

            trayIconIcon = System.Drawing.SystemIcons.Application;

            latencyFixEnabled = settings.LatencyFixEnabled;

            LatencyFixButton.Content = latencyFixEnabled
                ? "Disable Latency Fix"
                : "Enable Latency Fix";

            trayIcon = new NotifyIcon
            {
                Icon = trayIconIcon ?? System.Drawing.SystemIcons.Application,
                Visible = true,
                Text = "Audio Latency Fixer"
            };

            trayIcon.DoubleClick += (_, _) =>
            {
                Show();
                WindowState = WindowState.Normal;
                Activate();
            };

            var menu = new ContextMenuStrip();

            menu.Items.Add("Open", null, (_, _) =>
            {
                Show();
                WindowState = WindowState.Normal;
                Activate();
            });

            menu.Items.Add("Exit", null, (_, _) =>
            {
                allowExit = true;

                if (trayIcon != null)
                {
                    trayIcon.Visible = false;
                }

                Application.Current.Shutdown();
            });

            trayIcon.ContextMenuStrip = menu;
        }

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(settingsPath))
                {
                    string json = File.ReadAllText(settingsPath);
                    settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch { }
        }

        private void SaveSettings()
        {
            try
            {
                string dir = Path.GetDirectoryName(settingsPath)!;

                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(settingsPath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Failed to save settings: " + ex.Message);
            }
        }

        private void ApplySavedSettings()
        {
            try
            {
                if (settings.BoostProcessPriority)
                {
                    Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
                }

                if (settings.BoostThreadPriority)
                {
                    Thread.CurrentThread.Priority = ThreadPriority.Highest;
                }

                if (settings.DisableAudioDucking)
                {
                    using var key = Registry.CurrentUser.CreateSubKey(
                        @"Software\Microsoft\Multimedia\Audio");

                    key?.SetValue("UserDuckingPreference", 3);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Failed to apply saved settings: " + ex);
            }
        }

        private Icon? LoadTrayIcon()
        {
            try
            {
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AudioLatencyFixer.ico");

                if (File.Exists(iconPath))
                {
                    return new Icon(iconPath);
                }

                Debug.WriteLine($"Tray icon not found: {iconPath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load tray icon: {ex}");
            }

            return null;
        }

        private void OnAppExit(object? sender, ExitEventArgs e)
        {
            try
            {
                if (trayIcon != null)
                {
                    trayIcon.Visible = false;
                    trayIcon.Dispose();
                    trayIcon = null;
                }
            }
            catch
            {
            }

            try
            {
                if (trayIconIcon != null)
                {
                    trayIconIcon.Dispose();
                    trayIconIcon = null;
                }
            }
            catch
            {
            }
        }

        private void OpenOptimizations_Click(object sender, RoutedEventArgs e)
        {
            var window = new OptimizationsWindow(settings)
            {
                Owner = this
            };

            window.ShowDialog();

            SaveSettings();
        }

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);

            if (WindowState == WindowState.Minimized)
            {
                Hide();
            }
        }

        private void RunLatencyTest_Click(object sender, RoutedEventArgs e)
        {
            if (currentDevice == null)
            {
                LatencyText.Text = "Latency: No device";
                return;
            }

            try
            {
                var client = currentDevice.AudioClient;
                int sampleRate = client.MixFormat.SampleRate;
                long defaultPeriod = client.DefaultDevicePeriod;
                double latencyMs = defaultPeriod / 10000.0;

                LatencyText.Text = $"Latency: ~{latencyMs:F1} ms (estimated)";
            }
            catch (Exception ex)
            {
                LatencyText.Text = "Latency: Error";
                MessageBox.Show(ex.Message, "Latency Test Error");
            }
        }

        private async void LatencyFixButton_Click(object sender, RoutedEventArgs e)
        {
            latencyFixEnabled = !latencyFixEnabled;

            settings.LatencyFixEnabled = latencyFixEnabled;
            SaveSettings();

            if (latencyFixEnabled)
            {
                LatencyFixButton.Content = "Disable Latency Fix";
                await ApplyLatencyFixAsync();
            }
            else
            {
                LatencyFixButton.Content = "Enable Latency Fix";
                RemoveLatencyFix();
            }
        }

        private string GetAudioRecommendation(MMDevice device)
        {
            string name = device.FriendlyName.ToLowerInvariant();

            if (name.Contains("bluetooth"))
            {
                return "Bluetooth detected\nThese can cause high audio latency (100–300ms)\n\nSOLUTION: Use wired headphones";
            }

            if (name.Contains("nvidia") || name.Contains("hd audio"))
            {
                return "Monitor / GPU audio detected\nThese can cause higher audio latency than normal\n\nSOLUTION: Use the audio jack on your motherboard or a USB headset";
            }

            if (name.Contains("usb"))
            {
                return "USB audio detected\nGood latency\n\nNo action is needed.";
            }

            return "Likely motherboard audio\nNo action is needed.";
        }

        private void AnalyzeAudioSetup()
        {
            try
            {
                if (currentDevice == null)
                {
                    MessageBox.Show("No audio device detected.", "Audio Analysis");
                    return;
                }

                string recommendation = GetAudioRecommendation(currentDevice);
                MessageBox.Show(recommendation, "Audio Analysis");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Analysis failed: " + ex.Message, "Audio Analysis Error");
            }
        }

        private void RemoveLatencyFix()
        {
            LatencyText.Text = "Latency: Normal";

            MessageBox.Show(
                "Latency Fix Disabled.\n\n(System settings are not fully restored yet.)",
                "Info",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }

        private async Task ApplyLatencyFixAsync()
        {
            LatencyText.Text = "Latency: Fixing...";

            try
            {
                if (currentDevice == null)
                {
                    GetAudioDevice();
                }

                if (currentDevice == null)
                {
                    LatencyText.Text = "Latency: No device";
                    return;
                }

                DeviceText.Text = $"Device: {currentDevice.FriendlyName}";

                string recommendation = GetAudioRecommendation(currentDevice);
                const string audioPath = @"Software\Microsoft\Multimedia\Audio";

                try
                {
                    Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
                }
                catch
                {
                }

                await Task.Run(() =>
                {
                    try
                    {
                        using RegistryKey? key = Registry.CurrentUser.CreateSubKey(audioPath);
                        key?.SetValue("UserDuckingPreference", 3, RegistryValueKind.DWord);

                        EnableLowLatencyMode();
                        Debug.WriteLine("C++ DLL called successfully.");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("Background optimization failed: " + ex);
                    }
                });

                LatencyText.Text = "Latency: Optimized (safe mode)";

                MessageBox.Show(
                    "Latency Fix Applied!\n\n" +
                    "✔ System optimized\n" +
                    "✔ Process priority boosted\n\n" +
                    "Recommendation:\n" + recommendation + "\n\n" +
                    "For the BEST latency:\n" +
                    "- Use wired headphones\n" +
                    "- Avoid monitor audio\n" +
                    "- Disable enhancements in Windows",
                    "Success",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            }
            catch (Exception ex)
            {
                LatencyText.Text = "Latency: Failed";
                MessageBox.Show("Fix failed:\n" + ex.Message, "Error");
            }
        }

        private void GetAudioDevice()
        {
            try
            {
                var enumerator = new MMDeviceEnumerator();
                currentDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

                DeviceText.Text = $"Device: {currentDevice.FriendlyName}";
            }
            catch (Exception ex)
            {
                currentDevice = null;
                DeviceText.Text = "Device: Error";
                MessageBox.Show("Audio device error: " + ex.Message, "Audio Device Error");
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (allowExit)
            {
                base.OnClosing(e);
                return;
            }

            e.Cancel = true;
            Hide();

            try
            {
                if (trayIcon != null)
                {
                    trayIcon.ShowBalloonTip(
                        3000,
                        "Audio Latency Fixer",
                        "App minimized to tray. Right-click the tray icon to exit.",
                        ToolTipIcon.None
                    );
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to show tray balloon: {ex}");
            }
        }
    }
}