using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
namespace CyberTrap;
public sealed class Coordinator(Action<string> log)
{
    readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(2) };
    public static async Task<JsonElement> Schedule(string op, string folder = "")
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var pipe = new NamedPipeClientStream(".", "CyberTrap.Launcher.Control.v1", PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(timeout.Token);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        await writer.WriteLineAsync(JsonSerializer.Serialize(new { op, folder }));
        var line = await reader.ReadLineAsync(timeout.Token) ?? throw new IOException("Schedule I bridge disconnected.");
        var result = JsonDocument.Parse(line).RootElement.Clone();
        if (result.TryGetProperty("ok", out var ok) && !ok.GetBoolean()) throw new InvalidOperationException(result.TryGetProperty("detail", out var detail) ? detail.GetString() : result.GetProperty("error").GetString());
        return result;
    }
    public async Task<JsonElement> Nivalis(Settings s, string op, Dictionary<string, string>? args = null)
    {
        var token = await File.ReadAllTextAsync(Path.Combine(s.Root("Nivalis"), "BepInEx", "cache", "nivalismodkit-bridge.token"));
        var query = string.Join('&', (args ?? new()).Select(x => Uri.EscapeDataString(x.Key) + "=" + Uri.EscapeDataString(x.Value)));
        using var request = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1:5710/cmd/" + Uri.EscapeDataString(op) + "?" + query) { Content = new ByteArrayContent([]) };
        request.Headers.Add("X-Kit-Token", token.Trim()); using var response = await http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Nivalis command failed (" + (int)response.StatusCode + "): " + body);
        var result = JsonDocument.Parse(body).RootElement.Clone();
        if (result.TryGetProperty("error", out var error)) throw new InvalidOperationException(error.ToString());
        return result;
    }
    static async Task<JsonElement> Wait(Func<Task<JsonElement>> read, Func<JsonElement, bool> ready, string failure)
    {
        var end = DateTime.UtcNow.AddMinutes(5); Exception? last = null;
        while (DateTime.UtcNow < end) { try { var value = await read(); if (ready(value)) return value; } catch (Exception ex) when (ex is IOException or HttpRequestException or InvalidOperationException or OperationCanceledException or JsonException) { last = ex; } await Task.Delay(1000); }
        throw new TimeoutException(failure + (last == null ? "" : " " + last.Message));
    }
    public async Task Launch(Settings s)
    {
        s.Validate(); log("Starting Cyberpunk..."); var cp = Windows.Start(s.Cyberpunk, false);
        log("Starting the background games. First loader generation can take several minutes...");
        var si = Windows.Start(s.Schedule, true); var nv = Windows.Start(s.Nivalis, true);
        await Wait(() => Task.FromResult(WindowReady(cp)), x => x.GetProperty("ready").GetBoolean(), "Cyberpunk window did not open."); Windows.Focus(cp);
        await Task.WhenAll(LoadNivalis(s, nv), LoadSchedule(s, si));
        Windows.Focus(cp);
        await Nivalis(s, "nc-maintenance", new() { ["enabled"] = "false" });
        log("Ready. Load your paired Cyberpunk save normally. Nivalis runs only while Cyberpunk renews its gameplay lease.");
    }
    async Task HideWhenReady(Settings s, System.Diagnostics.Process process, string name)
    {
        await Wait(() => Task.FromResult(JsonSerializer.SerializeToElement(new { ready = Windows.GameWindow(process) != 0 })), x => x.GetProperty("ready").GetBoolean(), name + " window did not open.");
        if (s.Hidden) { Windows.Hide(process); log(name + " hidden; native loading and rendering remain enabled."); }
    }
    async Task LoadNivalis(Settings s, System.Diagnostics.Process nv)
    {
        await HideWhenReady(s, nv, "Nivalis");
        log("Waiting for Nivalis's native title menu...");
        // A menu object can become interactable while the logo-scene coroutine is
        // still finishing. Require several consecutive idle samples before loading.
        var stable = 0;
        var state = await Wait(() => Nivalis(s, "nc-status"), x => { if (x.GetProperty("ready").GetBoolean()) return true; if (x.TryGetProperty("titleReady", out var title) && title.GetBoolean() && !x.GetProperty("loading").GetBoolean()) stable++; else stable = 0; return stable >= 6; }, "Nivalis did not reach a stable title menu. Show the background games and check the loader log.");
        if (!state.GetProperty("ready").GetBoolean()) { log("Loading the selected Nivalis sandbox..."); await Nivalis(s, "nc-load-lab"); }
        state = await Wait(() => Nivalis(s, "nc-status"), x => x.GetProperty("ready").GetBoolean(), "Nivalis sandbox did not finish loading. No second load was sent.");
        if (state.GetProperty("save").GetString() != s.LabSave) throw new InvalidOperationException("Nivalis has a different save loaded. Close it and start the selected sandbox; this launcher will not overwrite or reload a live save.");
        if (state.TryGetProperty("paperTest", out var paper) && paper.GetBoolean()) throw new InvalidOperationException("The current Nivalis session is an isolated paper test. Restart Nivalis before launching paid operations.");
        await Nivalis(s, "nc-maintenance", new() { ["enabled"] = "true" });
        if (s.Prepare) await Prepare(s);
        log("Nivalis sandbox connected.");
    }
    async Task LoadSchedule(Settings s, System.Diagnostics.Process si)
    {
        await HideWhenReady(s, si, "Schedule I");
        log("Connecting Schedule I...");
        var observe = await Wait(() => Schedule("status"), _ => true, "Schedule I launcher bridge did not start. Check MelonLoader generation.");
        if (!observe.GetProperty("loaded").GetBoolean()) { await Task.Delay(2000); log("Loading the selected Schedule I native save..."); await Schedule("load", s.ScheduleSave); }
        observe = await Wait(() => Schedule("status"), x => x.GetProperty("loaded").GetBoolean(), "Schedule I save did not load. Select a native save once in its Continue menu.");
        var session = observe.GetProperty("folder").GetString() ?? "";
        if (s.ScheduleSave != "" && !string.Equals(Path.GetFullPath(session), Path.GetFullPath(s.ScheduleSave), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Schedule I loaded a different save from the selected pair. Show it, choose the selected save in Continue, then launch again.");
        s.ScheduleSave = session; s.Save();
        await Schedule("resume");
        log("Schedule I save connected and resumed.");
    }
    static JsonElement WindowReady(System.Diagnostics.Process p) { p.Refresh(); return JsonSerializer.SerializeToElement(new { ready = !p.HasExited && p.MainWindowHandle != 0 }); }
    async Task Prepare(Settings s)
    {
        var snapshot = await Nivalis(s, "nc-venues");
        if (snapshot.GetProperty("venues").EnumerateArray().Count(v => v.GetProperty("owned").GetBoolean()) >= 15 && snapshot.GetProperty("venues").EnumerateArray().Where(v => v.GetProperty("owned").GetBoolean()).All(v => v.GetProperty("ready").GetBoolean() && v.GetProperty("menu").GetArrayLength() > 0)) { log("Existing business setup is ready; preserving it."); return; }
        log("Preparing the dedicated business copy, with operations held...");
        await Nivalis(s, "nc-setup", new() { ["save"] = s.LabSave }); snapshot = await Nivalis(s, "nc-venues");
        var ids = snapshot.GetProperty("venues").EnumerateArray().Where(v => v.GetProperty("owned").GetBoolean()).Select(v => v.GetProperty("id").GetString()!).ToArray();
        foreach (var id in ids) { await Nivalis(s, "nc-provision", new() { ["save"] = s.LabSave, ["venue"] = id }); await Nivalis(s, "nc-seating", new() { ["save"] = s.LabSave, ["venue"] = id, ["tables"] = "8" }); }
        await Nivalis(s, "nc-setup", new() { ["save"] = s.LabSave });
        foreach (var id in ids) { await Nivalis(s, "nc-service-hours", new() { ["save"] = s.LabSave, ["venue"] = id, ["start"] = "8", ["end"] = "20" }); await Nivalis(s, "nc-manager-action", new() { ["save"] = s.LabSave, ["venue"] = id, ["action"] = "staff" }); }
        await Nivalis(s, "nc-save", new() { ["save"] = s.LabSave }); log("Business setup checkpointed. Ingredients are purchased through V's real eddy reservations.");
    }
}
