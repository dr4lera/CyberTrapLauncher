using System.IO;
namespace CyberTrap;
public static class Tests
{
    public static async Task InstallFixture()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "install-fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        Settings.TestHome = Path.Combine(root, "profile"); Settings.TestSaves = Path.Combine(root, "saves"); Directory.CreateDirectory(Settings.Saves);
        var settings = new Settings { Cyberpunk = Path.Combine(root, "cp", "bin", "x64", "Cyberpunk2077.exe"), Schedule = Path.Combine(root, "si", "Schedule I.exe"), Nivalis = Path.Combine(root, "nv", "Nivalis Nights.exe"), LabSave = "save_fixture", Prepare = false };
        if (settings.Root("Cyberpunk") != Path.Combine(root, "cp")) throw new Exception("Cyberpunk installation root is incorrect.");
        foreach (var exe in new[] { settings.Cyberpunk, settings.Schedule, settings.Nivalis }) { Directory.CreateDirectory(Path.GetDirectoryName(exe)!); File.WriteAllBytes(exe, []); }
        foreach (var game in new[] { "Schedule", "Nivalis" }) File.WriteAllBytes(Path.Combine(settings.Root(game), "GameAssembly.dll"), []);
        File.WriteAllText(Path.Combine(Settings.Saves, "save_fixture.sav"), "fixture");
        await new Installer(_ => { }).Run(settings);
        if (!File.Exists(Path.Combine(settings.Root("Schedule"), "Mods", "CyberTrap.Launcher.Guest.dll")) || !File.Exists(Path.Combine(settings.Root("Nivalis"), "BepInEx", "plugins", "NivalisNightCity.dll"))) throw new Exception("Install fixture missing mods.");
        var bank = Path.Combine(settings.Root("Cyberpunk"), "red4ext", "plugins", "NivalisNightCity", "transactions"); Directory.CreateDirectory(bank); File.WriteAllText(Path.Combine(bank, "pending.json"), "{\"status\":\"reserved\"}");
        settings.LabSave = "save_other"; File.WriteAllText(Path.Combine(Settings.Saves, "save_other.sav"), "fixture"); bool rejected = false;
        try { await new Installer(_ => { }).Run(settings); } catch (InvalidOperationException ex) when (ex.Message.Contains("payment")) { rejected = true; }
        if (!rejected) throw new Exception("Pair change with pending payment was accepted.");
        settings.LabSave = "save_fixture";
        var changed = Path.Combine(settings.Root("Schedule"), "Mods", "CyberTrap.Launcher.Guest.dll");
        File.WriteAllText(changed, "user modification");
        Installer.RestoreLatest(settings);
        if (File.ReadAllText(changed) != "user modification" || File.Exists(Path.Combine(settings.Root("Nivalis"), "BepInEx", "plugins", "NivalisNightCity.dll"))) throw new Exception("Restore did not retain user changes or remove unchanged installed files.");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "install-test-result.txt"), "PASS: official downloads, full extraction, renderer, helper, configuration, pending-payment guard, restoration preserving user changes. Fixture: " + root);
        Settings.TestHome = Settings.TestSaves = null;
    }
    public static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "CyberTrap-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var unity = root.Replace('\\', '/'); var native = Path.Combine(unity, "Saves", "account", "SaveGame_1");
        if (NativeSavePath.Rebuild(unity, Path.GetFullPath(native)) != native) throw new Exception("Native save identity was normalized and broke the paired gameplay bridge.");
        foreach (var bad in new[] { "../escape", @"..\escape", @"C:\escape", "a:b" }) { bool rejected = false; try { Installer.SafePath(root, bad); } catch (InvalidDataException) { rejected = true; } if (!rejected) throw new Exception("Path traversal was accepted."); }
        if (Installer.SafePath(root, "plugins/mod.dll") != Path.Combine(root, "plugins", "mod.dll")) throw new Exception("Safe path failed.");
        var ini = "[One]\nEnabled=false\n[DevBridge]\nEnabled=false\nPort=1\n[Other]\nEnabled=false\n";
        var result = Ini.Set(ini, "DevBridge", "Enabled", "true"); if (!result.Contains("[One]\nEnabled=false") || !result.Contains("[DevBridge]\nEnabled=true") || !result.Contains("[Other]\nEnabled=false")) throw new Exception("INI update altered unrelated settings.");
        result = Ini.Set(result, "DevBridge", "AllowCommands", "true"); if (!result.Contains("AllowCommands=true\n[Other]")) throw new Exception("INI insertion escaped its section.");
        if (Installer.Mods.Select(p => p.Game).Distinct().Count() != 3 || Installer.Mods.Any(p => p.Sha256.Length != 64)) throw new Exception("Mod release pinning failed.");
        Directory.Delete(root); File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "test-result.txt"), "PASS: traversal, valid paths, section-preserving configuration and three-game pins.");
    }
}
