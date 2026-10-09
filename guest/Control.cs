using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.UI;
using MelonLoader;
using UnityEngine;
[assembly: MelonInfo(typeof(CyberTrap.Guest.Control), "CyberTrap Launcher Control", "0.1.0", "dr4lera / Codex")]
[assembly: MelonGame("TVGS", "Schedule I")]
namespace CyberTrap.Guest;
public sealed class Control : MelonMod
{
    readonly CancellationTokenSource stop = new();
    readonly ConcurrentQueue<(JsonElement Request, TaskCompletionSource<string> Reply)> queue = new();
    public override void OnInitializeMelon() => _ = Server();
    public override void OnDeinitializeMelon() => stop.Cancel();
    static object Status() => new { ok = true, loaded = LoadManager.Instance?.IsGameLoaded == true, loading = LoadManager.Instance?.IsLoading == true, folder = LoadManager.Instance?.LoadedGameFolderPath, paused = PauseMenu.Instance?.IsPaused };
    public override void OnUpdate()
    {
        Application.runInBackground = true;
        while (queue.TryDequeue(out var task))
        {
            try
            {
                var op = task.Request.GetProperty("op").GetString();
                if (op == "resume") { if (LoadManager.Instance?.IsGameLoaded != true) throw new InvalidOperationException("save_not_loaded"); if (PauseMenu.Instance?.IsPaused == true) PauseMenu.Instance.Resume(); }
                else if (op == "load")
                {
                    var manager = LoadManager.Instance ?? throw new InvalidOperationException("title_not_ready");
                    string folder = task.Request.TryGetProperty("folder", out var chosen) ? chosen.GetString() ?? "" : "";
                    if (folder != "")
                    {
                        folder = CyberTrap.NativeSavePath.Rebuild(Application.persistentDataPath, folder);
                        if (!File.Exists(Path.Combine(folder, "Game.json"))) throw new InvalidOperationException("select_native_save_folder");
                    }
                    if (manager.IsGameLoaded) { if (folder != "" && !string.Equals(Path.GetFullPath(manager.LoadedGameFolderPath), folder, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("different_save_already_loaded"); }
                    else if (!manager.IsLoading)
                    {
                        var info = LoadManager.LastPlayedGame;
                        if (folder != "") { var slot = 0; var name = Path.GetFileName(folder); if (name.StartsWith("SaveGame_")) int.TryParse(name[9..], out slot); if (!LoadManager.TryLoadSaveInfo(folder, slot, out info, true)) throw new InvalidOperationException("native_save_not_readable"); }
                        if (info == null) throw new InvalidOperationException("select_a_save_once_in_continue");
                        manager.StartGame(info, false, true);
                    }
                }
                else if (op != "status") throw new InvalidOperationException("unknown_operation");
                task.Reply.TrySetResult(JsonSerializer.Serialize(Status()));
            }
            catch (Exception ex) { task.Reply.TrySetResult(JsonSerializer.Serialize(new { ok = false, error = ex.Message })); }
        }
    }
    async Task Server()
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream("CyberTrap.Launcher.Control.v1", PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stop.Token);
                using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true); using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                var text = await reader.ReadLineAsync(); if (text == null || text.Length > 4096) continue;
                var request = JsonDocument.Parse(text).RootElement.Clone(); var reply = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously); queue.Enqueue((request, reply));
                var result = await reply.Task.WaitAsync(TimeSpan.FromSeconds(15), stop.Token); await writer.WriteLineAsync(result);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) when (ex is IOException or TimeoutException or JsonException) { }
        }
    }
}
