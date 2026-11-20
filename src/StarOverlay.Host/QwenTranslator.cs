using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using StarOverlay.Core;
using StarOverlay.Framework;

namespace StarOverlay.Host;

internal static class QwenTranslator
{
    private const int Port = 8779;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(45) };
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly ConcurrentQueue<string> Queue = new();
    private static readonly ConcurrentDictionary<string, byte> Pending = new(StringComparer.Ordinal);
    private static Process? _process;
    private static int _worker;
    private static int _unavailable;
    private static int _startFailures;
    private static int _listening;
    private static int _serverEpoch;
    private static bool _owned;
    private static bool _runningGpu;

    public static void Listen()
    {
        if (Interlocked.Exchange(ref _listening, 1) != 0) return;
        TranslationPrompt.Changed += () =>
        {
            while (Queue.TryDequeue(out _)) { }
            Pending.Clear();
        };
        // a new device, a newly picked model or finished downloads: start the server again with what is there now
        Action restart = () =>
        {
            Interlocked.Exchange(ref _startFailures, 0);
            Volatile.Write(ref _unavailable, 0);
            Interlocked.Increment(ref _serverEpoch);
            EnsureWorker();
            _ = Task.Run(Restart);
        };
        TranslationDevice.Changed += () => { if (ChatGloss.TranslationWanted) restart(); };
        TranslationModel.Changed += () => { if (ChatGloss.TranslationWanted) restart(); };
        // translation switched off, or the chat plugin switched off: nothing is waited for, and the model leaves the graphics card
        ChatGloss.Changed += () =>
        {
            if (ChatGloss.TranslationWanted) return;
            while (Queue.TryDequeue(out _)) { }
            Pending.Clear();
            Interlocked.Increment(ref _serverEpoch); // a line being translated now is dropped, not retried
            _ = Task.Run(Release);
        };
    }

    public static void Request(string text)
    {
        Listen();
        if (string.IsNullOrWhiteSpace(text) || Volatile.Read(ref _unavailable) != 0 || !ChatGloss.TranslationWanted) return;
        EnsureWorker();
        if (FindModel() is null) return;
        if (!Pending.TryAdd(text, 1)) return;
        Queue.Enqueue(text);
    }

    public static void Stop()
    {
        try
        {
            if (!Gate.Wait(8000)) return;
            try
            {
                StopListener();
            }
            finally
            {
                Gate.Release();
            }
        }
        catch (Exception)
        {
        }
    }

    private static void EnsureWorker()
    {
        if (Volatile.Read(ref _unavailable) != 0) return;
        if (Interlocked.CompareExchange(ref _worker, 1, 0) != 0) return;
        Task.Run(async () =>
        {
            try
            {
                await Loop();
            }
            finally
            {
                Interlocked.Exchange(ref _worker, 0);
            }
        });
    }

    private static async Task Loop()
    {
        var announced = false;
        while (Volatile.Read(ref _unavailable) == 0 && ChatGloss.TranslationWanted) // the worker ends when translation is not wanted; the next request starts it again
        {
            if (FindLlama() is null)
            {
                Volatile.Write(ref _unavailable, 1);
                return;
            }

            if (FindModel() is null)
            {
                await Task.Delay(1000);
                continue;
            }

            if (!announced)
            {
                announced = true;
                JapaneseGloss.Touch();
            }

            if (!Queue.TryDequeue(out var text))
            {
                await Task.Delay(40);
                continue;
            }

            var generation = TranslationPrompt.Generation;
            var prompt = TranslationPrompt.Current;
            var epoch = Volatile.Read(ref _serverEpoch);
            try
            {
                var korean = await Translate(text, prompt);
                Pending.TryRemove(text, out _);
                if (korean.Length > 0) JapaneseGloss.Store(text, korean);
            }
            catch (Exception)
            {
                if (generation != TranslationPrompt.Generation || epoch != Volatile.Read(ref _serverEpoch))
                {
                    Pending.TryRemove(text, out _);
                    continue;
                }
                Pending.TryRemove(text, out _);
                if (_process is { HasExited: true } && Interlocked.Increment(ref _startFailures) >= 2)
                {
                    Volatile.Write(ref _unavailable, 1);
                    return;
                }

                await Task.Delay(1500);
            }
        }
    }

    private static async Task<string> Translate(string text, string prompt)
    {
        await EnsureServer();
        var json = await Post(text, prompt, true);
        if (json is null) json = await Post(text, prompt, false);
        return json is null ? "" : Clean(json, text);
    }

    private static async Task<string?> Post(string text, string prompt, bool thinking)
    {
        object payload = thinking
            ? new
            {
                messages = Messages(text, prompt),
                temperature = 0.2,
                max_tokens = 120, // a long chat line must not be cut in the middle
                chat_template_kwargs = new { enable_thinking = false },
            }
            : new
            {
                messages = Messages(text, prompt),
                temperature = 0.2,
                max_tokens = 120, // a long chat line must not be cut in the middle
            };
        using var response = await Http.PostAsync(
            $"http://127.0.0.1:{Port}/v1/chat/completions",
            new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadAsStringAsync();
    }

    private static object[] Messages(string text, string prompt) =>
    [
        new { role = "system", content = prompt },
        new { role = "user", content = text },
    ];

    private static string Clean(string json, string source)
    {
        using var doc = JsonDocument.Parse(json);
        var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
        return TranslationText.Clean(content, source); // one chat-ready line: no emoji, no runaway repetition, every line kept
    }

    private static async Task Restart()
    {
        await Gate.WaitAsync();
        try
        {
            StopListener();
            if (ChatGloss.TranslationWanted) await StartCurrentAsync();
            JapaneseGloss.Touch();
        }
        catch (Exception)
        {
            StopListener();
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Stops the llama-server (the model leaves the graphics card and memory) when translation is not wanted.</summary>
    private static async Task Release()
    {
        await Gate.WaitAsync();
        try
        {
            if (!ChatGloss.TranslationWanted) StopListener();
        }
        catch (Exception)
        {
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task EnsureServer()
    {
        if (!ChatGloss.TranslationWanted) throw new InvalidOperationException("translation is switched off");
        var gpu = TranslationDevice.UseGpu;
        if (Serving(gpu) && await Healthy()) return;
        await Gate.WaitAsync();
        try
        {
            gpu = TranslationDevice.UseGpu;
            if (Serving(gpu) && await Healthy()) return;
            if (_owned || await Healthy()) StopListener();
            await StartCurrentAsync();
        }
        finally
        {
            Gate.Release();
        }
    }

    private static bool Serving(bool gpu) => _owned && _runningGpu == gpu && _process is { HasExited: false };

    private static async Task StartCurrentAsync()
    {
        var gpu = TranslationDevice.UseGpu;
        var exe = FindLlama() ?? throw new FileNotFoundException("llama-server");
        var model = FindModel() ?? throw new FileNotFoundException("qwen");
        var start = new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = Path.GetDirectoryName(exe) ?? AppStorage.Root(),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        if (gpu) start.Environment.Remove("CUDA_VISIBLE_DEVICES");
        else start.Environment["CUDA_VISIBLE_DEVICES"] = "-1";
        start.ArgumentList.Add("-m");
        start.ArgumentList.Add(model);
        start.ArgumentList.Add("--host");
        start.ArgumentList.Add("127.0.0.1");
        start.ArgumentList.Add("--port");
        start.ArgumentList.Add(Port.ToString());
        start.ArgumentList.Add("-ngl");
        start.ArgumentList.Add(gpu ? "99" : "0");
        start.ArgumentList.Add("-t");
        start.ArgumentList.Add("6");
        start.ArgumentList.Add("-tb");
        start.ArgumentList.Add("6");
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("2048");
        start.ArgumentList.Add("-np");
        start.ArgumentList.Add("1");
        start.ArgumentList.Add("-fa");
        start.ArgumentList.Add("off");
        start.ArgumentList.Add("--no-warmup");
        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        try
        {
            if (!process.Start()) throw new InvalidOperationException("llama-server");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _process = process;
            _owned = true;
            _runningGpu = gpu;
            var deadline = DateTime.UtcNow.AddSeconds(180);
            while (DateTime.UtcNow < deadline)
            {
                if (process.HasExited) throw new InvalidOperationException("llama-server exited");
                if (await Healthy()) return;
                await Task.Delay(300);
            }

            throw new TimeoutException("llama-server");
        }
        catch
        {
            Release(process);
            if (ReferenceEquals(_process, process))
            {
                _process = null;
                _owned = false;
            }

            throw;
        }
    }

    private static void StopListener()
    {
        var owned = _process;
        _process = null;
        _owned = false;
        Release(owned);
        var pid = ListenerPid();
        if (pid <= 0 || pid == Environment.ProcessId) return;
        try
        {
            using var other = Process.GetProcessById(pid);
            if (!other.ProcessName.Contains("llama", StringComparison.OrdinalIgnoreCase)) return;
            Release(other);
        }
        catch (Exception)
        {
        }
    }

    private static void Release(Process? process)
    {
        if (process is null) return;
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(8000);
            }
        }
        catch (Exception)
        {
        }

        try
        {
            process.CancelOutputRead();
            process.CancelErrorRead();
        }
        catch (Exception)
        {
        }

        process.Dispose();
    }

    private static int ListenerPid()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "netstat",
                Arguments = "-ano -p tcp",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            });
            if (process is null) return 0;
            var text = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(3000)) return 0;
            foreach (var raw in text.Split('\n'))
            {
                var parts = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 5 || !parts[1].EndsWith(":" + Port, StringComparison.Ordinal)) continue;
                if (int.TryParse(parts[^1], out var pid)) return pid;
            }
        }
        catch (Exception)
        {
            return 0;
        }

        return 0;
    }

    private static async Task<bool> Healthy()
    {
        try
        {
            using var response = await Http.GetAsync($"http://127.0.0.1:{Port}/health");
            return response.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // where the programs and the model are (and which model the user picked) is TranslationModel's business
    private static string? FindLlama() => TranslationModel.LlamaPath(TranslationDevice.UseGpu);

    private static string? FindModel() => TranslationModel.ModelPath();
}
