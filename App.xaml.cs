using System.IO;
using System.Windows;
namespace CyberTrap;
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--restore-windows")) { CyberTrap.Windows.Restore(); Shutdown(0); return; }
        if (e.Args.Contains("--restore-install")) { try { Installer.RestoreLatest(Settings.Load()); Shutdown(0); } catch (Exception ex) { File.WriteAllText(Path.Combine(Settings.Home, "restore-result.txt"), ex.ToString()); Shutdown(1); } return; }
        if (e.Args.Contains("--launch-test"))
        {
            var file = Path.Combine(Settings.Home, "launch-test.log"); Directory.CreateDirectory(Settings.Home);
            try { var settings = Settings.Load(); await new Coordinator(line => File.AppendAllText(file, line + Environment.NewLine)).Launch(settings); File.AppendAllText(file, "PASS: startup completed\n"); Shutdown(0); }
            catch (Exception ex) { CyberTrap.Windows.Restore(); File.AppendAllText(file, "FAIL: " + ex + "\n"); Shutdown(1); }
            return;
        }
        if (e.Args.Contains("--install-test")) { try { await Tests.InstallFixture(); Shutdown(0); } catch (Exception ex) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "test-failure.txt"), ex.ToString()); Shutdown(1); } return; }
        if (e.Args.Contains("--self-test")) { try { Tests.Run(); Shutdown(0); } catch (Exception ex) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "test-failure.txt"), ex.ToString()); Shutdown(1); } return; }
        if (e.Args.Contains("--install"))
        {
            try { await new Installer(_ => { }).Run(Settings.Load()); File.WriteAllText(Path.Combine(Settings.Home, "install-result.txt"), "OK"); Shutdown(0); }
            catch (Exception ex) { Directory.CreateDirectory(Settings.Home); File.WriteAllText(Path.Combine(Settings.Home, "install-result.txt"), ex.Message); Shutdown(1); } return;
        }
        new MainWindow().Show();
    }
}
