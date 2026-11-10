using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;

namespace SeldonLattice.API.Services;

/// <summary>
/// Manages launching and stopping the local llama.cpp chat and embedding servers.
/// Tracks spawned PIDs so the frontend can start/stop them
/// without shelling out from the browser.
/// </summary>
public class ServerLaunchService(ILogger<ServerLaunchService> logger, IConfiguration config)
{
    // llama.cpp server binary. Configure LlamaCpp:Executable with the full path when the
    // binary is not on PATH (the WindowsApps stub is blocked by Device Guard).
    private string LlamaExe => config["LlamaCpp:Executable"] ?? "llama-server";

    // 30B MoE RAM-offload profile. Leave the GPU layer count and tensor buffer placement unset
    // so llama.cpp's --fit can choose how many layers fit in currently free VRAM; the remaining
    // weights stay in system RAM. Avoid --n-cpu-moe here because it sets tensor buffer overrides,
    // which prevent this llama.cpp build's fit routine from adjusting GPU placement. -c 16384
    // --parallel 1 gives the single chat slot the full 16k context. --ctx-size-slot is NOT a flag
    // in the installed llama.cpp build (0.6.0-dev, build 11429), so it was replaced rather than
    // appended verbatim. -fa on is explicit because flash-attention's value is optional here.
    private const string ExtraArgs = @"--fit on -c 16384 --parallel 1 -fa on";

    // The embedding model is tiny (274 MB) and CPU embedding is fast enough for the handful of
    // strings a level needs. Keep it off the GPU: the chat and embedding servers are launched
    // back-to-back, and since the chat model grew to 30B they can race for VRAM. The embedding
    // process then dies during startup while the launcher still reports it as launched.
    //
    // llama.cpp defaults the physical micro-batch (-ub) to 512 tokens. The expanded word-count
    // settings produce inputs longer than that (a 547-token prompt was rejected with HTTP 500),
    // so raise both the logical and physical batches. The model context still fits the current
    // word-count ceiling; a further increase should also raise -c / reduce --parallel.
    private const string EmbeddingArgs = "-ngl 0 -c 2048 -b 2048 -ub 2048";

    // Track spawned processes by server key so we can stop them later.
    private static readonly ConcurrentDictionary<string, Process> Spawned = new();

    // Tail of redirected process output per server, kept so a process that dies during startup can say
    // why in the launch result instead of leaving only a connection-refused at request time.
    private static readonly ConcurrentDictionary<string, string> LastOutput = new();
    private static readonly ConcurrentDictionary<string, byte> ExpectedStops = new();
    private const int ErrorTailLines = 40;

    private const int ChatPort = 9931;
    private const int EmbeddingPort = 8081;

    private string ModelsRoot => config["Models:Root"]!;

    /// <summary>Absolute path to the chat model, from config with the repo layout as default.</summary>
    private string ChatModelPath =>
        Path.GetFullPath(Path.Combine(ModelsRoot, config["Models:Chat"]!));

    /// <summary>
    /// Absolute path to the embedding model. Read by <c>Program.cs</c> via
    /// <c>Models:Embedding</c>; kept here as well so the launcher and the client cannot
    /// disagree about which file is in use.
    /// </summary>
    private string EmbeddingModelPath =>
        Path.GetFullPath(Path.Combine(ModelsRoot, config["Models:Embedding"]!));

    /// <summary>
    /// The name the server advertises and the client requests. Derived from the file name so
    /// it cannot drift from the model actually loaded - the client previously sent a full
    /// Windows path where the server expected an alias.
    /// </summary>
    private string ChatModelAlias => Path.GetFileNameWithoutExtension(ChatModelPath);

    public ServerStatus GetStatus()
    {
        return new ServerStatus
        {
            Chat = CheckServer(ChatPort, "chat"),
            Embeddings = CheckServer(EmbeddingPort, "embeddings"),
        };
    }

    public async Task<LaunchResult> LaunchAllAsync()
    {
        var result = new LaunchResult();

        // llama.cpp chat (9931)
        if (IsPortOpen(ChatPort))
        {
            result.AlreadyRunning.Add("chat");
        }
        else if (IsProcessRunning("chat"))
        {
            result.Starting.Add("chat");
        }
        else
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = LlamaExe,
                    Arguments = $"serve -m \"{ChatModelPath}\" --jinja --host 127.0.0.1 --port {ChatPort} --alias {ChatModelAlias} {ExtraArgs}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                var proc = StartWithDrain(psi, "chat");
                Spawned["chat"] = proc;
                logger.LogInformation("Launched llama.cpp chat (PID {Pid})", proc.Id);

                // The 30B model can take longer than this grace period. Report that state
                // explicitly instead of claiming the server is ready before it binds.
                var (chatState, chatReason) = await WaitForStartupAsync(proc, ChatPort, 5000);
                if (chatState == StartupState.Exited)
                {
                    result.Errors.Add($"chat: {DescribeFailure("chat", chatReason)}");
                    logger.LogError("llama.cpp chat failed to start: {Reason}", chatReason);
                }
                else if (chatState == StartupState.Timeout)
                {
                    result.Starting.Add("chat");
                    logger.LogInformation("llama.cpp chat is still loading: {Reason}", chatReason);
                }
                else
                {
                    result.Launched.Add("chat");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to launch llama.cpp chat");
                result.Errors.Add($"chat: {ex.Message}");
            }
        }

        // llama.cpp embeddings (8081)
        if (IsPortOpen(EmbeddingPort))
        {
            result.AlreadyRunning.Add("embeddings");
        }
        else if (IsProcessRunning("embeddings"))
        {
            result.Starting.Add("embeddings");
        }
        else
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = LlamaExe,
                    Arguments = $"serve -m \"{EmbeddingModelPath}\" --embedding --pooling mean {EmbeddingArgs} --host 127.0.0.1 --port {EmbeddingPort} --alias nomic-embed-text-v1.5",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                var proc = StartWithDrain(psi, "embeddings");
                Spawned["embeddings"] = proc;
                logger.LogInformation("Launched llama.cpp embeddings (PID {Pid})", proc.Id);

                // The embedding model is small, so it should be listening quickly. Report a
                // failure here rather than letting the first ascent discover it halfway
                // through a stream, where the response has already been sent.
                var (embeddingState, embeddingReason) = await WaitForStartupAsync(proc, EmbeddingPort, 30000);
                if (embeddingState != StartupState.Open)
                {
                    result.Errors.Add($"embeddings: {DescribeFailure("embeddings", embeddingReason)}");
                    logger.LogError("llama.cpp embeddings failed to start: {Reason}", embeddingReason);
                }
                else
                {
                    result.Launched.Add("embeddings");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to launch llama.cpp embeddings");
                result.Errors.Add($"embeddings: {ex.Message}");
            }
        }

        return result;
    }

    public async Task<LaunchResult> LaunchServerAsync(string key)
    {
        var result = new LaunchResult();

        switch (key)
        {
            case "chat":
                if (IsPortOpen(ChatPort)) { result.AlreadyRunning.Add(key); return result; }
                if (IsProcessRunning(key)) { result.Starting.Add(key); return result; }
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = LlamaExe,
                        Arguments = $"serve -m \"{ChatModelPath}\" --jinja --host 127.0.0.1 --port {ChatPort} --alias {ChatModelAlias} {ExtraArgs}",
                        UseShellExecute = false, CreateNoWindow = true,
                        RedirectStandardOutput = true, RedirectStandardError = true,
                    };
                    var proc = StartWithDrain(psi, "chat");
                    Spawned["chat"] = proc;
                    logger.LogInformation("Launched llama.cpp chat (PID {Pid})", proc.Id);

                    var (state, reason) = await WaitForStartupAsync(proc, ChatPort, 5000);
                    if (state == StartupState.Exited)
                    {
                        result.Errors.Add($"{key}: {DescribeFailure("chat", reason)}");
                        logger.LogError("llama.cpp chat failed to start: {Reason}", reason);
                    }
                    else if (state == StartupState.Timeout)
                    {
                        result.Starting.Add(key);
                        logger.LogInformation("llama.cpp chat is still loading: {Reason}", reason);
                    }
                    else
                    {
                        result.Launched.Add(key);
                    }
                }
                catch (Exception ex) { result.Errors.Add($"{key}: {ex.Message}"); }
                break;

            case "embeddings":
                if (IsPortOpen(EmbeddingPort)) { result.AlreadyRunning.Add(key); return result; }
                if (IsProcessRunning(key)) { result.Starting.Add(key); return result; }
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = LlamaExe,
                        Arguments = $"serve -m \"{EmbeddingModelPath}\" --embedding --pooling mean {EmbeddingArgs} --host 127.0.0.1 --port {EmbeddingPort} --alias nomic-embed-text-v1.5",
                        UseShellExecute = false, CreateNoWindow = true,
                        RedirectStandardOutput = true, RedirectStandardError = true,
                    };
                    var proc = StartWithDrain(psi, "embeddings");
                    Spawned["embeddings"] = proc;
                    logger.LogInformation("Launched llama.cpp embeddings (PID {Pid})", proc.Id);

                    var (state, reason) = await WaitForStartupAsync(proc, EmbeddingPort, 30000);
                    if (state != StartupState.Open)
                    {
                        result.Errors.Add($"{key}: {DescribeFailure("embeddings", reason)}");
                        logger.LogError("llama.cpp embeddings failed to start: {Reason}", reason);
                    }
                    else
                    {
                        result.Launched.Add(key);
                    }
                }
                catch (Exception ex) { result.Errors.Add($"{key}: {ex.Message}"); }
                break;

            default:
                result.Errors.Add($"{key}: unknown server");
                break;
        }

        return result;
    }

    public async Task<StopResult> StopServerAsync(string key)
    {
        var result = new StopResult();

        if (Spawned.TryRemove(key, out var proc))
        {
            try
            {
                if (!proc.HasExited)
                {
                    ExpectedStops[key] = 0;
                    proc.Kill(entireProcessTree: true);
                    await proc.WaitForExitAsync();
                    result.Stopped.Add(key);
                    logger.LogInformation("Stopped {Key} (PID {Pid})", key, proc.Id);
                }
            }
            catch (Exception ex)
            {
                ExpectedStops.TryRemove(key, out _);
                result.Errors.Add($"{key}: {ex.Message}");
            }
        }
        else
        {
            // Not tracked - try to find and kill by port
            int port = key switch
            {

                "chat" => ChatPort,
                "embeddings" => EmbeddingPort,
                _ => 0,
            };
            if (port > 0)
            {
                try
                {
                    var pid = GetPidForPort(port);
                    if (pid > 0)
                    {
                        var p = Process.GetProcessById(pid);
                        p.Kill(entireProcessTree: true);
                        await p.WaitForExitAsync();
                        result.Stopped.Add(key);
                        logger.LogInformation("Stopped {Key} by port lookup (PID {Pid})", key, pid);
                    }
                    else
                    {
                        result.Errors.Add($"{key}: no process found on port {port}");
                    }
                }
                catch (Exception ex)
                {
                    ExpectedStops.TryRemove(key, out _);
                    result.Errors.Add($"{key}: {ex.Message}");
                }
            }
            else
            {
                result.Errors.Add($"{key}: unknown server");
            }
        }

        return result;
    }

    public async Task<StopResult> StopAllAsync()
    {
        var result = new StopResult();

        foreach (var (key, proc) in Spawned)
        {
            try
            {
                if (!proc.HasExited)
                {
                    ExpectedStops[key] = 0;
                    proc.Kill(entireProcessTree: true);
                    await proc.WaitForExitAsync();
                    result.Stopped.Add(key);
                    logger.LogInformation("Stopped {Key} (PID {Pid})", key, proc.Id);
                }
            }
            catch (Exception ex)
            {
                ExpectedStops.TryRemove(key, out _);
                logger.LogError(ex, "Failed to stop {Key}", key);
                result.Errors.Add($"{key}: {ex.Message}");
            }
        }

        Spawned.Clear();
        return result;
    }

    private static int GetPidForPort(int port)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netstat",
                Arguments = $"-ano -p tcp",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var proc = Process.Start(psi);
            if (proc == null) return 0;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();
            foreach (var line in output.Split('\n'))
            {
                if (line.Contains($"127.0.0.1:{port}") && line.Contains("LISTENING"))
                {
                    var parts = line.Trim().Split([' '], StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 5 && int.TryParse(parts[4], out var pid))
                        return pid;
                }
            }
        }
        catch
        {
            // ignored
        }

        return 0;
    }

    private enum StartupState
    {
        Open,
        Exited,
        Timeout,
    }

    /// <summary>
    /// Waits for a launched server to open its port, or to exit. Returns Timeout when the
    /// process is still alive but has not bound yet - callers decide whether that is normal
    /// (chat loading a large model) or a failure (the small embedding model).
    /// </summary>
    private static async Task<(StartupState State, string Message)> WaitForStartupAsync(
        Process proc, int port, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (proc.HasExited)
                return (StartupState.Exited,
                    $"exited with code {proc.ExitCode} before listening on port {port}");

            if (IsPortOpen(port))
                return (StartupState.Open, string.Empty);

            await Task.Delay(250);
        }

        return IsPortOpen(port)
            ? (StartupState.Open, string.Empty)
            : (StartupState.Timeout, $"did not listen on port {port} within {timeoutMs / 1000}s");
    }

    /// <summary>Combines a startup failure with the tail of the child's output, if any.</summary>
    private static string DescribeFailure(string key, string reason) =>
        LastOutput.TryGetValue(key, out var output) && output.Length > 0
            ? $"{reason}. Output: {output}"
            : reason;

    /// <summary>
    /// Start a process and asynchronously drain its redirected stdout/stderr.
    /// Without a drain, the OS pipe buffer (~64KB) fills and the child blocks on its
    /// next write - the process stays alive (idle CPU) but stops responding to any
    /// request that produces output, wedging the API behind it.
    /// </summary>
    private Process StartWithDrain(ProcessStartInfo psi, string key)
    {
        // Drop the previous run's tail so a fresh failure cannot be described by stale output.
        LastOutput.TryRemove(key, out _);

        var p = Process.Start(psi);
        if (p is null) return null!;

        var tail = new ConcurrentQueue<string>();
        var drains = new List<Task>(2);
        if (psi.RedirectStandardOutput)
            drains.Add(CaptureTailAsync(p, p.StandardOutput, key, "stdout", tail));
        if (psi.RedirectStandardError)
            drains.Add(CaptureTailAsync(p, p.StandardError, key, "stderr", tail));

        _ = MonitorProcessExitAsync(p, key, Task.WhenAll(drains), tail);

        return p;
    }

    /// <summary>
    /// Drains a redirected stream and keeps its last lines. The tail is what a failed server
    /// says on the way out (missing model, bad flag, CUDA OOM), and it is otherwise lost when
    /// the process is not attached to a console.
    /// </summary>
    private async Task CaptureTailAsync(
        Process process, StreamReader reader, string key, string streamName, ConcurrentQueue<string> tail)
    {
        try
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                var entry = $"[{streamName}] {line}";
                tail.Enqueue(entry);
                while (tail.Count > ErrorTailLines)
                    tail.TryDequeue(out _);

                // A process can exit and be relaunched before its redirected streams finish
                // draining. Keep its output out of the new process's startup diagnostics.
                if (!Spawned.TryGetValue(key, out var current) || current.Id == process.Id)
                    LastOutput[key] = string.Join(Environment.NewLine, tail.ToArray());

                if (ContainsSeverity(line, "fatal", "[error]", "error:", "failed"))
                    logger.LogError("llama.cpp {Server} {Stream}: {Output}", key, streamName, line);
                else if (ContainsSeverity(line, "[warning]", "[warn]", "warning:"))
                    logger.LogWarning("llama.cpp {Server} {Stream}: {Output}", key, streamName, line);
                else
                    logger.LogDebug("llama.cpp {Server} {Stream}: {Output}", key, streamName, line);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Stopped reading llama.cpp {Server} {Stream}", key, streamName);
        }
    }

    private async Task MonitorProcessExitAsync(
        Process process, string key, Task outputDrains, ConcurrentQueue<string> tail)
    {
        try
        {
            await process.WaitForExitAsync();
            await outputDrains;

            var requested = ExpectedStops.TryRemove(key, out _);
            var exitCode = process.ExitCode;
            var output = string.Join(Environment.NewLine, tail.ToArray());

            if (requested || exitCode == 0)
            {
                logger.LogInformation(
                    "llama.cpp {Server} process exited with code {ExitCode} (requestedStop={RequestedStop}). Output: {Output}",
                    key, exitCode, requested, output);
            }
            else
            {
                logger.LogError(
                    "llama.cpp {Server} process stopped unexpectedly with code {ExitCode}. Output: {Output}",
                    key, exitCode, output);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not monitor llama.cpp {Server} process exit", key);
        }
    }

    private static bool ContainsSeverity(string line, params string[] markers) =>
        markers.Any(marker => line.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static bool IsPortOpen(int port)
    {
        try
        {
            using var client = new TcpClient();
            client.Connect("127.0.0.1", port);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static ServerInfo CheckServer(int port, string key)
    {
        var running = IsPortOpen(port);
        return new ServerInfo
        {
            Running = running,
            Starting = !running && IsProcessRunning(key),
            Port = port,
        };
    }

    private static bool IsProcessRunning(string key) =>
        Spawned.TryGetValue(key, out var process) && !process.HasExited;
}

public sealed record ServerStatus
{

    public required ServerInfo Chat { get; init; }
    public required ServerInfo Embeddings { get; init; }

    public bool AllRunning => Chat.Running && Embeddings.Running;
}

public sealed record ServerInfo
{
    public required bool Running { get; init; }
    public required bool Starting { get; init; }
    public required int Port { get; init; }
}

public sealed record LaunchResult
{
    public List<string> Launched { get; } = [];
    public List<string> Starting { get; } = [];
    public List<string> AlreadyRunning { get; } = [];
    public List<string> Errors { get; } = [];
}

public sealed record StopResult
{
    public List<string> Stopped { get; } = [];
    public List<string> Errors { get; } = [];
}
