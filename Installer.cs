using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace CyberTrap;
public sealed record Package(string Name, string Game, string Url, string Sha256, string Format = "zip", string Probe = "");
public sealed class Installer(Action<string> log)
{
    public static Action<double?>? DownloadProgress;
    public static void RestoreLatest(Settings settings)
    {
        settings.Validate(); foreach (var exe in new[] { settings.Cyberpunk, settings.Schedule, settings.Nivalis }) if (Windows.Find(exe) != null) throw new InvalidOperationException("Close the selected games before restoring installation files.");
        var backups = Path.Combine(Settings.Home, "backups");
        var journal = Directory.Exists(backups) ? Directory.GetFiles(backups, "restore.json", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() : null;
        if (journal == null) throw new InvalidOperationException("No installation backup is available.");
        var roots = new[] { settings.Root("Cyberpunk"), settings.Root("Schedule"), settings.Root("Nivalis") };
        foreach (var entry in JsonDocument.Parse(File.ReadAllText(journal)).RootElement.EnumerateArray().Reverse())
        {
            var path = entry.GetProperty("path").GetString()!;
            var root = roots.FirstOrDefault(r => Path.GetFullPath(path).StartsWith(Path.GetFullPath(r) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException("Backup target is outside the selected games.");
            SafePath(root, Path.GetRelativePath(root, path));
            if (File.Exists(path) && Hash(path) != entry.GetProperty("installedHash").GetString()) continue;
            var prior = entry.GetProperty("backup");
            if (prior.ValueKind == JsonValueKind.String) { var saved = prior.GetString()!; if (!Path.GetFullPath(saved).StartsWith(Path.GetDirectoryName(journal)! + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid backup file."); File.Copy(saved, path, true); }
            else if (File.Exists(path)) File.Delete(path);
        }
        File.Move(journal, journal + ".restored", false);
    }
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(10) };
    readonly List<(string Path, string? Backup, string InstalledHash)> journal = new();
    string backup = "";
    public static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    public static string SafePath(string root, string relative)
    {
        if (Path.IsPathRooted(relative) || relative.Contains(':')) throw new InvalidDataException("Unsafe archive entry.");
        var result = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!result.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Archive entry escapes the game folder.");
        var parent = Path.GetDirectoryName(result);
        while (parent != null && parent.Length >= Path.GetFullPath(root).Length) { if (Directory.Exists(parent) && (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Installation through a directory link is not allowed."); parent = Path.GetDirectoryName(parent); }
        if (File.Exists(result) && (File.GetAttributes(result) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Installation through a file link is not allowed.");
        return result;
    }
    public static async Task<string> Download(Package package)
    {
        var uri = new Uri(package.Url); if (uri.Scheme != "https" || !new[] { "github.com", "raw.githubusercontent.com", "builds.bepinex.dev", "reshade.me" }.Contains(uri.Host)) throw new InvalidDataException("Untrusted download source.");
        var cache = Path.Combine(Settings.Home, "downloads"); Directory.CreateDirectory(cache);
        var file = Path.Combine(cache, package.Sha256.ToLowerInvariant());
        if (File.Exists(file) && Hash(file).Equals(package.Sha256, StringComparison.OrdinalIgnoreCase)) return file;
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead); response.EnsureSuccessStatusCode();
        await using (var input = await response.Content.ReadAsStreamAsync()) await using (var output = File.Create(file + ".part"))
        {
            var bytes = new byte[131072]; long total = 0; int count;
            while ((count = await input.ReadAsync(bytes)) > 0) { await output.WriteAsync(bytes.AsMemory(0, count)); total += count; DownloadProgress?.Invoke(response.Content.Headers.ContentLength is long length && length > 0 ? 100.0 * total / length : null); }
        }
        DownloadProgress?.Invoke(null);
        if (!Hash(file + ".part").Equals(package.Sha256, StringComparison.OrdinalIgnoreCase)) { File.Delete(file + ".part"); throw new InvalidDataException("Download checksum failed: " + package.Name); }
        File.Move(file + ".part", file, true); return file;
    }
    void Put(string target, byte[] data, string[] roots)
    {
        var digest = Convert.ToHexString(SHA256.HashData(data)); if (File.Exists(target) && Hash(target) == digest) return;
        foreach (var exe in roots) if (Windows.Find(exe) != null) throw new InvalidOperationException("Close the selected games before installing or updating files.");
        var prior = File.Exists(target) ? Path.Combine(backup, journal.Count + ".bak") : null;
        if (prior != null) File.Copy(target, prior);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        journal.Add((target, prior, digest)); SaveJournal();
        File.WriteAllBytes(target + ".cybertrap.tmp", data); File.Move(target + ".cybertrap.tmp", target, true);
    }
    void SaveJournal() => File.WriteAllText(Path.Combine(backup, "restore.json"), JsonSerializer.Serialize(journal.Select(x => new { path = x.Path, backup = x.Backup, installedHash = x.InstalledHash }), new JsonSerializerOptions { WriteIndented = true }));
    static byte[] Read(ZipArchiveEntry entry) { if (entry.Length > 300_000_000) throw new InvalidDataException("Oversized archive entry."); using var stream = entry.Open(); using var bytes = new MemoryStream(); stream.CopyTo(bytes); return bytes.ToArray(); }
    public async Task Run(Settings settings)
    {
        settings.Validate(); backup = Path.Combine(Settings.Home, "backups", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(backup);
        string[] exes = [settings.Cyberpunk, settings.Schedule, settings.Nivalis];
        try
        {
            GuardPairChange(settings);
            await Runtime();
            var packages = JsonSerializer.Deserialize<List<Package>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "frameworks.json")), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            foreach (var package in packages)
            {
                log("Checking " + package.Name + "..."); var root = settings.Root(package.Game); var file = await Download(package);
                using var zip = ZipFile.OpenRead(file);
                var probe = zip.GetEntry(package.Probe);
                if (probe != null && File.Exists(SafePath(root, package.Probe)) && Hash(SafePath(root, package.Probe)) == Convert.ToHexString(SHA256.HashData(Read(probe)))) continue;
                // Existing loader proxies may belong to unrelated software. Never replace them silently.
                foreach (var proxy in zip.Entries.Where(e => e.FullName.Equals("winhttp.dll", StringComparison.OrdinalIgnoreCase) || e.FullName.Equals("version.dll", StringComparison.OrdinalIgnoreCase) || e.FullName.EndsWith("/winmm.dll", StringComparison.OrdinalIgnoreCase)))
                    if (File.Exists(SafePath(root, proxy.FullName)) && Hash(SafePath(root, proxy.FullName)) != Convert.ToHexString(SHA256.HashData(Read(proxy)))) throw new InvalidOperationException("An existing loader proxy conflicts with " + package.Name + ". Back it up and resolve that conflict before installation.");
                foreach (var entry in zip.Entries.Where(e => !e.FullName.EndsWith('/'))) Put(SafePath(root, entry.FullName), Read(entry), exes);
            }
            await ReShade(settings, exes);
            foreach (var package in Mods)
            {
                log("Checking " + package.Name + "..."); using var zip = ZipFile.OpenRead(await Download(package));
                foreach (var entry in zip.Entries.Where(e => !e.FullName.EndsWith('/') && !e.FullName.StartsWith("CyberTrap-Docs/"))) Put(SafePath(settings.Root(package.Game), entry.FullName), Read(entry), exes);
            }
            var helper = Path.Combine(AppContext.BaseDirectory, "payload", "CyberTrap.Launcher.Guest.dll");
            var expected = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "payload", "CyberTrap.Launcher.Guest.dll.sha256")).Trim();
            if (!Hash(helper).Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Bundled launcher helper checksum failed.");
            Put(SafePath(settings.Root("Schedule"), "Mods/CyberTrap.Launcher.Guest.dll"), File.ReadAllBytes(helper), exes);
            GuardPairChange(settings); EnsureSave(settings); Configure(settings, exes); settings.Save();
            log("Mods and frameworks ready. Backup: " + backup);
        }
        catch
        {
            foreach (var row in journal.AsEnumerable().Reverse()) { if (row.Backup != null) File.Copy(row.Backup, row.Path, true); else if (File.Exists(row.Path) && Hash(row.Path) == row.InstalledHash) File.Delete(row.Path); }
            throw;
        }
    }
    async Task Runtime()
    {
        var shared = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "shared", "Microsoft.NETCore.App");
        if (Settings.TestHome != null || Directory.Exists(shared) && Directory.GetDirectories(shared).Any(p => Path.GetFileName(p).StartsWith("6."))) return;
        if (!new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator)) throw new UnauthorizedAccessException("Administrator access is required to install the .NET 6 prerequisite.");
        log("Installing the official .NET 6 runtime required by MelonLoader...");
        var info = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "runtime.json"))).RootElement;
        var uri = new Uri(info.GetProperty("url").GetString()!); if (uri.Scheme != "https" || uri.Host != "builds.dotnet.microsoft.com") throw new InvalidDataException("Untrusted runtime source.");
        var file = Path.Combine(Settings.Home, "dotnet-runtime.exe"); Directory.CreateDirectory(Settings.Home);
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead); response.EnsureSuccessStatusCode();
        await using (var output = File.Create(file)) await response.Content.CopyToAsync(output);
        using (var input = File.OpenRead(file)) if (!Convert.ToHexString(SHA512.HashData(input)).Equals(info.GetProperty("sha512").GetString(), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Runtime checksum failed.");
        var start = new System.Diagnostics.ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "/install", "/quiet", "/norestart" }) start.ArgumentList.Add(arg);
        using var installer = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("Runtime installer did not start."); await installer.WaitForExitAsync();
        if (installer.ExitCode != 0 && installer.ExitCode != 3010) throw new InvalidOperationException("Runtime installation failed: " + installer.ExitCode);
    }
    public static readonly Package[] Mods = [
        new("CyberTrap Cyberpunk", "Cyberpunk", "https://github.com/dr4lera/CyberpunkStreetChem/releases/download/v0.3.0/CyberTrap-Cyberpunk2077-v0.3.0.zip", "1068AF73962F5717501E6BDC42A714EE81872DD0F2088498C9D506E5B7CE3BE6"),
        new("CyberTrap Schedule I", "Schedule", "https://github.com/dr4lera/CyberpunkStreetChem/releases/download/v0.3.0/CyberTrap-ScheduleI-v0.3.0.zip", "CA0EFAB96132E9D84FFE61BBD65D59C473036217D82953C522389ED59B2EAC23"),
        new("CyberTrap Nivalis", "Nivalis", "https://github.com/dr4lera/CyberpunkStreetChem/releases/download/v0.3.0/CyberTrap-NivalisNights-v0.3.0.zip", "54308550AEAC3BFD13FD0C9D8A5578E47E1FED5B6FBDBE1D8A7AA2135ABCFA59") ];
    async Task ReShade(Settings s, string[] exes)
    {
        var root = Path.GetDirectoryName(s.Cyberpunk)!; var dll = Path.Combine(root, "dxgi.dll");
        if (!File.Exists(dll))
        {
            log("Installing ReShade with full add-on support...");
            var file = await Download(new("ReShade", "Cyberpunk", "https://reshade.me/downloads/ReShade_Setup_6.8.0_Addon.exe", "AFE4C8F13048306307983B8B3D41D5BF00A86820440B0E57DEA10950E1176445"));
            var bytes = File.ReadAllBytes(file); bool found = false;
            for (var i = 0; i < bytes.Length - 4; i++) if (bytes[i] == 0x50 && bytes[i + 1] == 0x4b && bytes[i + 2] == 3 && bytes[i + 3] == 4)
            {
                try { using var stream = new MemoryStream(bytes, i, bytes.Length - i); using var zip = new ZipArchive(stream); var entry = zip.GetEntry("ReShade64.dll"); if (entry == null) continue; Put(dll, Read(entry), exes); found = true; break; } catch (InvalidDataException) { }
            }
            if (!found) throw new InvalidDataException("Official ReShade archive could not be extracted.");
        }
        else if (System.Diagnostics.FileVersionInfo.GetVersionInfo(dll).ProductName?.Contains("ReShade", StringComparison.OrdinalIgnoreCase) != true) throw new InvalidOperationException("Cyberpunk's dxgi.dll belongs to another graphics mod. Resolve the proxy conflict first.");
        var shader = Path.Combine(root, "streetchem", "shaders", "ReShade.fxh");
        var existing = Path.Combine(root, "reshade-shaders", "Shaders", "ReShade.fxh");
        if (!File.Exists(shader))
        {
            if (File.Exists(existing)) Put(shader, File.ReadAllBytes(existing), exes);
            else { var include = JsonSerializer.Deserialize<Package>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "shader.json")), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!; Put(shader, File.ReadAllBytes(await Download(include)), exes); }
        }
        var ini = Path.Combine(root, "ReShade.ini"); var text = File.Exists(ini) ? File.ReadAllText(ini) : "[GENERAL]\n";
        var effects = Regex.Match(text, @"(?m)^EffectSearchPaths=(.*)$").Groups[1].Value.Trim(); if (!effects.Contains("streetchem", StringComparison.OrdinalIgnoreCase)) text = Ini.Set(text, "GENERAL", "EffectSearchPaths", (effects.TrimEnd(',') + @",.\streetchem\shaders").TrimStart(','));
        var preset = Regex.Match(text, @"(?m)^PresetPath=(.*)$").Groups[1].Value.Trim(); if (preset == "") { preset = @".\streetchem\CyberTrapPreset.ini"; text = Ini.Set(text, "GENERAL", "PresetPath", preset); }
        var presetPath = Path.GetFullPath(Path.Combine(root, preset)); if (!presetPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("ReShade's preset is outside the game folder. Select a local preset before automatic setup.");
        var presetText = File.Exists(presetPath) ? File.ReadAllText(presetPath) : "";
        foreach (var key in new[] { "Techniques", "TechniqueSorting" }) { var value = Regex.Match(presetText, "(?m)^" + key + "=(.*)$").Groups[1].Value.Trim(); if (!value.Contains("StreetChemLab@StreetChemLab.fx")) presetText = Ini.Set(presetText, "", key, (value.TrimEnd(',') + ",StreetChemLab@StreetChemLab.fx").TrimStart(',')); }
        Put(ini, System.Text.Encoding.UTF8.GetBytes(text), exes); Put(presetPath, System.Text.Encoding.UTF8.GetBytes(presetText), exes);
    }
    static void EnsureSave(Settings s)
    {
        if (s.LabSave != "") return;
        var source = Path.GetFullPath(s.SourceSave); if (!source.StartsWith(Path.GetFullPath(Settings.Saves) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || Path.GetExtension(source) != ".sav") throw new InvalidOperationException("Select a native Nivalis save from its profile folder.");
        var sourceSidecar = Path.ChangeExtension(source, ".modkit.json");
        if (File.Exists(sourceSidecar) && File.ReadAllText(sourceSidecar).Contains("dr4lera.nivalis.nightcity")) throw new InvalidOperationException("This save already contains business bridge history. Keep using its configured sandbox rather than copying a paired financial history.");
        var name = "save_" + DateTime.Now.ToString("yyyy_MM_dd_HH_mm_ss"); var target = Path.Combine(Settings.Saves, name + ".sav");
        File.Copy(source, target, false); var sidecar = Path.ChangeExtension(source, ".modkit.json"); if (File.Exists(sidecar)) File.Copy(sidecar, Path.Combine(Settings.Saves, name + ".modkit.json"), false);
        s.LabSave = name;
    }
    static void GuardPairChange(Settings s)
    {
        var dir = Path.Combine(s.Root("Cyberpunk"), "red4ext", "plugins", "NivalisNightCity"); var cfg = Path.Combine(dir, "NivalisNightCity.json");
        if (!File.Exists(cfg)) return;
        var current = JsonDocument.Parse(File.ReadAllText(cfg)).RootElement;
        if (current.TryGetProperty("save", out var save) && save.GetString() == s.LabSave && s.LabSave != "") return;
        var transactions = Path.Combine(dir, "transactions"); if (!Directory.Exists(transactions)) return;
        foreach (var ticket in Directory.GetFiles(transactions, "*.json")) { var state = JsonDocument.Parse(File.ReadAllText(ticket)).RootElement.GetProperty("status").GetString(); if (state != "settled" && state != "cancelled_refunded") throw new InvalidOperationException("Finish the current business payment before changing the paired save."); }
    }
    void Configure(Settings s, string[] exes)
    {
        var cfg = Path.Combine(s.Root("Nivalis"), "BepInEx", "config", "dr4lera.nivalis.nightcity.cfg"); var text = File.Exists(cfg) ? File.ReadAllText(cfg) : "";
        foreach (var row in new[] { ("Lab", "SaveName", s.LabSave), ("Bridge", "CyberpunkPath", s.Root("Cyberpunk")), ("Bridge", "CentralSupplies", "true"), ("Sandbox", "AutoSetup", "false"), ("Sandbox", "KeepStaffHappy", "true") }) text = Ini.Set(text, row.Item1, row.Item2, row.Item3);
        Put(cfg, System.Text.Encoding.UTF8.GetBytes(text), exes);
        cfg = Path.Combine(s.Root("Nivalis"), "BepInEx", "config", "BepInEx.cfg"); text = File.Exists(cfg) ? File.ReadAllText(cfg) : "";
        text = Ini.Set(text, "Logging.Console", "Enabled", "false");
        Put(cfg, System.Text.Encoding.UTF8.GetBytes(text), exes);
        cfg = Path.Combine(s.Root("Nivalis"), "BepInEx", "config", "bgasm.nivalis.modkit.cfg"); text = File.Exists(cfg) ? File.ReadAllText(cfg) : "";
        foreach (var row in new[] { ("Enabled", "true"), ("AllowCommands", "true"), ("Port", "5710") }) text = Ini.Set(text, "DevBridge", row.Item1, row.Item2);
        Put(cfg, System.Text.Encoding.UTF8.GetBytes(text), exes);
        cfg = Path.Combine(s.Root("Cyberpunk"), "red4ext", "plugins", "NivalisNightCity", "NivalisNightCity.json");
        var data = File.Exists(cfg) ? JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(cfg))! : new();
        data["save"] = JsonSerializer.SerializeToElement(s.LabSave); data["nivalisGame"] = JsonSerializer.SerializeToElement(s.Root("Nivalis")); data.TryAdd("autoRestock", JsonSerializer.SerializeToElement(true)); data.Remove("developerManagerTravel");
        Put(cfg, System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true })), exes);
    }
}
public static class Ini
{
    public static string Set(string text, string section, string key, string value)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList(); string current = ""; int start = section == "" ? 0 : -1, end = lines.Count;
        for (int i = 0; i < lines.Count; i++) { var match = Regex.Match(lines[i], @"^\[([^\]]+)\]"); if (match.Success) { if (start >= 0 && current == section) { end = i; break; } current = match.Groups[1].Value; if (current == section) start = i + 1; } else if (current == section && Regex.IsMatch(lines[i], "^" + Regex.Escape(key) + @"\s*=")) { lines[i] = key + "=" + value; return string.Join('\n', lines); } }
        if (start < 0) { lines.Add("[" + section + "]"); lines.Add(key + "=" + value); } else lines.Insert(end, key + "=" + value);
        return string.Join('\n', lines);
    }
}
