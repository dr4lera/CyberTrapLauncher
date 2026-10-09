using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace CyberTrap;
public sealed class Settings
{
    public string Cyberpunk { get; set; } = "";
    public string Schedule { get; set; } = "";
    public string Nivalis { get; set; } = "";
    public string LabSave { get; set; } = "";
    public string SourceSave { get; set; } = "";
    public string ScheduleSave { get; set; } = "";
    public bool Prepare { get; set; } = true;
    public bool Hidden { get; set; } = true;
    internal static string? TestHome;
    internal static string? TestSaves;
    public static string Home => TestHome ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CyberTrapLauncher");
    public static string FileName => Path.Combine(Home, "settings.json");
    public static string Saves => TestSaves ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "ION LANDS", "Nivalis Nights");
    public static Settings Load() => File.Exists(FileName) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(FileName)) ?? new() : new();
    public void Save() { Directory.CreateDirectory(Home); File.WriteAllText(FileName + ".tmp", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); File.Move(FileName + ".tmp", FileName, true); }
    public string Root(string game) => game switch { "Cyberpunk" => Directory.GetParent(Path.GetDirectoryName(Path.GetDirectoryName(Cyberpunk))!)!.FullName, "Schedule" => Path.GetDirectoryName(Schedule)!, "Nivalis" => Path.GetDirectoryName(Nivalis)!, _ => throw new ArgumentException("Unknown game") };
    public void Validate()
    {
        foreach (var row in new[] { (Cyberpunk, "Cyberpunk2077.exe"), (Schedule, "Schedule I.exe"), (Nivalis, "Nivalis Nights.exe") })
            if (!File.Exists(row.Item1) || !string.Equals(Path.GetFileName(row.Item1), row.Item2, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Select the correct EXE for " + row.Item2);
        if (!File.Exists(Path.Combine(Root("Schedule"), "GameAssembly.dll")) || !File.Exists(Path.Combine(Root("Nivalis"), "GameAssembly.dll"))) throw new InvalidOperationException("The guest mods require the IL2CPP editions of Schedule I and Nivalis.");
        if (string.IsNullOrEmpty(LabSave) && !File.Exists(SourceSave)) throw new InvalidOperationException("Select a Nivalis save first. Play Nivalis and create a native save if you have none.");
        if (!string.IsNullOrEmpty(LabSave) && (Path.GetFileName(LabSave) != LabSave || !File.Exists(Path.Combine(Saves, LabSave + ".sav")))) throw new InvalidOperationException("The selected Nivalis sandbox is missing.");
    }
    public void Detect()
    {
        var steam = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")?.GetValue("SteamPath") as string;
        var libraries = new List<string>(); if (steam != null) libraries.Add(steam);
        if (steam != null && File.Exists(Path.Combine(steam, "steamapps", "libraryfolders.vdf")))
            libraries.AddRange(Regex.Matches(File.ReadAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf")), "\"path\"\\s+\"([^\"]+)\"").Select(m => m.Groups[1].Value.Replace(@"\\", @"\")));
        foreach (var library in libraries.Distinct()) foreach (var row in new[] { ("1091500", "Cyberpunk", @"bin\x64\Cyberpunk2077.exe"), ("3164500", "Schedule", "Schedule I.exe"), ("1488490", "Nivalis", "Nivalis Nights.exe") })
        {
            var manifest = Path.Combine(library, "steamapps", "appmanifest_" + row.Item1 + ".acf"); if (!File.Exists(manifest)) continue;
            var match = Regex.Match(File.ReadAllText(manifest), "\"installdir\"\\s+\"([^\"]+)\"");
            var exe = Path.Combine(library, "steamapps", "common", match.Groups[1].Value, row.Item3); if (!File.Exists(exe)) continue;
            if (row.Item2 == "Cyberpunk" && Cyberpunk == "") Cyberpunk = exe; if (row.Item2 == "Schedule" && Schedule == "") Schedule = exe; if (row.Item2 == "Nivalis" && Nivalis == "") Nivalis = exe;
        }
        if (Nivalis != "")
        {
            var cfg = Path.Combine(Root("Nivalis"), "BepInEx", "config", "dr4lera.nivalis.nightcity.cfg");
            if (File.Exists(cfg))
            {
                var text = File.ReadAllText(cfg); var save = Regex.Match(text, @"(?m)^SaveName\s*=\s*(.+)$").Groups[1].Value.Trim();
                if (LabSave == "" && SourceSave == "" && File.Exists(Path.Combine(Saves, save + ".sav"))) LabSave = save;
                var cp = Regex.Match(text, @"(?m)^CyberpunkPath\s*=\s*(.+)$").Groups[1].Value.Trim();
                if (Cyberpunk == "" && File.Exists(Path.Combine(cp, "bin", "x64", "Cyberpunk2077.exe"))) Cyberpunk = Path.Combine(cp, "bin", "x64", "Cyberpunk2077.exe");
            }
        }
    }
}
