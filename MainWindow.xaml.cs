using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
namespace CyberTrap;
public partial class MainWindow : Window
{
    Settings settings;
    public MainWindow() { InitializeComponent(); try { settings = Settings.Load(); } catch { settings = new(); } settings.Detect(); Render(); Closing += (_, e) => { if (!LaunchButton.IsEnabled) { e.Cancel = true; Log("Wait for the current operation to finish before closing."); } else Read(); }; Closed += (_, _) => Windows.Restore(); Log("Select your games and a Nivalis sandbox. Install once, then launch."); }
    void Log(string text) => Dispatcher.Invoke(() => { LogBox.AppendText(text + Environment.NewLine); LogBox.ScrollToEnd(); if (!LaunchButton.IsEnabled) { Progress.IsIndeterminate = true; ProgressLabel.Text = "Working..."; } });
    void Render() { CyberpunkExe.Text = settings.Cyberpunk; ScheduleExe.Text = settings.Schedule; NivalisExe.Text = settings.Nivalis; PrepareBusinesses.IsChecked = settings.Prepare; HideGuests.IsChecked = settings.Hidden; NivalisSaveLabel.Text = settings.LabSave != "" ? "Sandbox: " + settings.LabSave : settings.SourceSave != "" ? "Copy from: " + Path.GetFileName(settings.SourceSave) : "Select a native Nivalis save."; ScheduleSaveLabel.Text = settings.ScheduleSave != "" ? "Paired Schedule I save: " + Path.GetFileName(settings.ScheduleSave) : "Schedule I: last played native save"; }
    void Read() { settings.Cyberpunk = CyberpunkExe.Text.Trim().Trim('"'); settings.Schedule = ScheduleExe.Text.Trim().Trim('"'); settings.Nivalis = NivalisExe.Text.Trim().Trim('"'); settings.Prepare = PrepareBusinesses.IsChecked == true; settings.Hidden = HideGuests.IsChecked == true; settings.Save(); }
    void BrowseGame(object sender, RoutedEventArgs e) { var dialog = new OpenFileDialog { Filter = "Game executable|*.exe" }; if (dialog.ShowDialog() != true) return; var game = (string)((System.Windows.Controls.Button)sender).Tag; if (game == "Cyberpunk") CyberpunkExe.Text = dialog.FileName; if (game == "Schedule") ScheduleExe.Text = dialog.FileName; if (game == "Nivalis") NivalisExe.Text = dialog.FileName; Read(); }
    void DetectSteam(object sender, RoutedEventArgs e) { Read(); settings.Detect(); settings.Save(); Render(); Log("Detected available Steam games. Browse manually for other installations."); }
    void ChooseNivalis(object sender, RoutedEventArgs e) { var dialog = new OpenFileDialog { Filter = "Nivalis native save|*.sav", InitialDirectory = Settings.Saves }; if (dialog.ShowDialog() != true) return; settings.SourceSave = dialog.FileName; settings.LabSave = ""; settings.Save(); Render(); }
    void ChooseSchedule(object sender, RoutedEventArgs e) { var dialog = new OpenFileDialog { Filter = "Schedule I save metadata|Game.json", InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "TVGS", "Schedule I", "Saves") }; if (dialog.ShowDialog() != true) return; settings.ScheduleSave = Path.GetDirectoryName(dialog.FileName)!; settings.Save(); Render(); Log("Selected native Schedule I save. A different live save will be left untouched."); }
    void ShowGuests(object sender, RoutedEventArgs e) { Windows.Restore(); Log("Background game windows restored."); }
    async void RestoreInstall(object sender, RoutedEventArgs e) => await Busy(() => { Installer.RestoreLatest(settings); Log("Restored the latest installation backup. Files changed since installation were kept."); return Task.CompletedTask; });
    async Task Busy(Func<Task> action)
    {
        InstallButton.IsEnabled = LaunchButton.IsEnabled = RestoreButton.IsEnabled = SettingsPanel.IsEnabled = false;
        Progress.IsIndeterminate = true; ProgressLabel.Text = "Working...";
        Installer.DownloadProgress = value => Dispatcher.Invoke(() => { Progress.IsIndeterminate = value == null; if (value != null) { Progress.Value = value.Value; ProgressLabel.Text = "Download " + Math.Round(value.Value) + "%"; } else ProgressLabel.Text = "Working..."; });
        try { Read(); await action(); Render(); Progress.IsIndeterminate = false; Progress.Value = 100; ProgressLabel.Text = "Complete"; }
        catch (Exception ex) { Windows.Restore(); Log("Stopped: " + ex.Message); Progress.IsIndeterminate = false; Progress.Value = 0; ProgressLabel.Text = "Needs attention"; MessageBox.Show(this, ex.Message, "CyberTrap needs attention", MessageBoxButton.OK, MessageBoxImage.Information); }
        finally { Installer.DownloadProgress = null; InstallButton.IsEnabled = LaunchButton.IsEnabled = RestoreButton.IsEnabled = SettingsPanel.IsEnabled = true; }
    }
    async Task InstallCore()
    {
        try { await new Installer(Log).Run(settings); }
        catch (UnauthorizedAccessException)
        {
            Log("Windows needs administrator access to install into the selected folders.");
            var result = Path.Combine(Settings.Home, "install-result.txt"); if (File.Exists(result)) File.Delete(result);
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" }; start.ArgumentList.Add("--install");
            using var helper = Process.Start(start) ?? throw new InvalidOperationException("Administrator installer did not start."); await helper.WaitForExitAsync();
            if (helper.ExitCode != 0 || !File.Exists(result) || File.ReadAllText(result) != "OK") throw new InvalidOperationException(File.Exists(result) ? File.ReadAllText(result) : "Installation was cancelled.");
            settings = Settings.Load(); Log("Administrator installation completed.");
        }
    }
    async void Install(object sender, RoutedEventArgs e) => await Busy(InstallCore);
    async void Launch(object sender, RoutedEventArgs e) => await Busy(async () => { await InstallCore(); await new Coordinator(Log).Launch(settings); });
}
