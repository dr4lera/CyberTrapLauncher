using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
namespace CyberTrap;
public static class Windows
{
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern nint GetStyle(nint h, int i);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern nint SetStyle(nint h, int i, nint value);
    [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(nint h, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] static extern bool SetWindowPos(nint h, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] static extern bool ShowWindow(nint h, int command);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(nint h);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(nint h, out uint pid);
    delegate bool EnumWindow(nint h, nint parameter);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindow callback, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(nint h, System.Text.StringBuilder text, int count);
    public static nint GameWindow(Process process)
    {
        nint found = 0;
        EnumWindows((h, _) => { GetWindowThreadProcessId(h, out uint pid); if (pid != process.Id) return true; var name = new System.Text.StringBuilder(128); GetClassName(h, name, name.Capacity); if (name.ToString() != "UnityWndClass") return true; found = h; return false; }, 0);
        return found;
    }
    sealed record WindowState(long Handle, int Pid, long Style, string Exe, long Started);
    static readonly Dictionary<nint, WindowState> hidden = new();
    static string StateFile => Path.Combine(Settings.Home, "background-windows.json");
    static void LoadHidden()
    {
        if (hidden.Count != 0 || !File.Exists(StateFile)) return;
        try { foreach (var row in JsonSerializer.Deserialize<List<WindowState>>(File.ReadAllText(StateFile)) ?? []) hidden[(nint)row.Handle] = row; } catch (JsonException) { }
    }
    public static void Hide(Process process)
    {
        LoadHidden();
        process.Refresh(); var h = GameWindow(process); if (h == 0) throw new InvalidOperationException("Guest game window is not ready yet.");
        if (hidden.TryGetValue(h, out var prior) && prior.Pid == process.Id && prior.Started == process.StartTime.ToUniversalTime().Ticks) return;
        var style = GetStyle(h, -20); hidden[h] = new((long)h, process.Id, (long)style, process.MainModule!.FileName, process.StartTime.ToUniversalTime().Ticks);
        Directory.CreateDirectory(Settings.Home); File.WriteAllText(StateFile, JsonSerializer.Serialize(hidden.Values));
        // Keep rendering a visible window; alpha zero and click-through conceal it without minimizing.
        SetStyle(h, -20, (nint)(((long)style & ~0x40000L) | 0x80L | 0x80000L | 0x20L | 0x08000000L));
        SetLayeredWindowAttributes(h, 0, 0, 2); SetWindowPos(h, 0, 0, 0, 0, 0, 0x37);
    }
    public static void Restore()
    {
        LoadHidden();
        foreach (var (h, info) in hidden)
        {
            GetWindowThreadProcessId(h, out uint pid); if (pid != info.Pid) continue;
            try { var p = Process.GetProcessById(info.Pid); if (p.StartTime.ToUniversalTime().Ticks != info.Started || !string.Equals(p.MainModule?.FileName, info.Exe, StringComparison.OrdinalIgnoreCase) || !new[] { "Schedule I.exe", "Nivalis Nights.exe" }.Contains(Path.GetFileName(info.Exe))) continue; } catch (ArgumentException) { continue; }
            SetLayeredWindowAttributes(h, 0, 255, 2); SetStyle(h, -20, (nint)info.Style); SetWindowPos(h, 0, 0, 0, 0, 0, 0x37); ShowWindow(h, 4);
        }
        hidden.Clear();
        if (File.Exists(StateFile)) File.Delete(StateFile);
    }
    public static void Focus(Process p) { p.Refresh(); if (p.MainWindowHandle != 0) { ShowWindow(p.MainWindowHandle, 9); SetForegroundWindow(p.MainWindowHandle); } }
    public static Process? Find(string exe) => Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)).FirstOrDefault(p => { try { return string.Equals(p.MainModule?.FileName, Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase); } catch { return false; } });
    public static Process Start(string exe, bool guest)
    {
        var current = Find(exe); if (current != null) return current;
        if (Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)).Length > 0) throw new InvalidOperationException("Another copy of " + Path.GetFileName(exe) + " is running. Close it first; the launcher will not start a duplicate guest.");
        var start = new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe)!, UseShellExecute = false };
        if (guest) { foreach (var arg in new[] { "-screen-fullscreen", "0", "-screen-width", "1280", "-screen-height", "720" }) start.ArgumentList.Add(arg); }
        if (Path.GetFileName(exe) == "Schedule I.exe") start.ArgumentList.Add("--melonloader.hideconsole");
        return Process.Start(start) ?? throw new InvalidOperationException("Could not start game.");
    }
}
