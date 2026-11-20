using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;

namespace StarOverlay.Framework;

/// <summary>
/// The translation tools on this PC: the llama-server programs (CPU and Vulkan builds) and the model files (.gguf).
/// It finds them, remembers which model the user picked, and downloads what is missing (checked by SHA-256).
/// Nothing is stored in the project: everything lives under <see cref="Root"/> (the data folder, set by the service).
/// </summary>
public static class TranslationModel
{
    public const string DefaultModelFile = "Qwen_Qwen3.5-4B-Q4_K_M.gguf";
    public const long DefaultModelBytes = 3_013_027_808;

    const string LlamaRelease = "https://github.com/ggml-org/llama.cpp/releases/download/b12377/";

    /// <summary>One thing to download. <see cref="Relative"/> is a path under <see cref="Root"/>; a zip is unpacked into that folder.</summary>
    public sealed record Piece(string Label, string Url, string Sha256, long Bytes, string Relative, bool Unzip);

    /// <summary>What "받기" fetches: the two llama-server builds (small) first, the model (about 3 GB) last.</summary>
    public static readonly IReadOnlyList<Piece> Pieces =
    [
        new("번역 도구(CPU)", LlamaRelease + "llama-b12377-bin-win-cpu-x64.zip", "d3b60c5abbe3a131c77e568310547492438bc7c785721eb765d2d817fe4424c5", 25_122_435, Path.Combine("llama", "cpu"), true),
        new("번역 도구(GPU)", LlamaRelease + "llama-b12377-bin-win-vulkan-x64.zip", "0162ff9acc36ef88575d673e56e16673f455fb685fea1857c96fb57dd4831ff7", 38_116_679, Path.Combine("llama", "vulkan"), true),
        new("번역 모델", "https://huggingface.co/bartowski/Qwen_Qwen3.5-4B-GGUF/resolve/main/" + DefaultModelFile, "13c16f426047e2de38cd075bdade4a7bcbc8c774384876f677740cda65f8a983", DefaultModelBytes, Path.Combine("models", DefaultModelFile), false),
    ];

    static readonly object Gate = new();
    static HttpClient? _shared;
    static string _root = "";
    static string _selected = "";
    static int _phase; // 0 idle, 1 downloading, 2 failed, 3 done
    static string _label = "";
    static string _failure = "";
    static long _done;
    static long _total;

    /// <summary>The data folder; <c>models</c> and <c>llama</c> are under it. Empty until the service sets it.</summary>
    public static string Root
    {
        get => _root;
        set => _root = value ?? "";
    }

    public static string ModelsDirectory => _root.Length == 0 ? "" : Path.Combine(_root, "models");

    public static string LlamaDirectory => _root.Length == 0 ? "" : Path.Combine(_root, "llama");

    /// <summary>The model file name the user picked ("" = automatic: the default model, else the first one found).</summary>
    public static string Selected => _selected;

    /// <summary>A model file that is about to be deleted: it is not offered or used any more, so the running translator lets go of it first.</summary>
    public static string Releasing { get; set; } = "";

    public static bool Downloading => Volatile.Read(ref _phase) == 1;

    /// <summary>Raised when the picked model or the files on disk changed, so a running translator should start again.</summary>
    public static event Action? Changed;

    public static void Apply(string? file)
    {
        var next = string.IsNullOrWhiteSpace(file) ? "" : Path.GetFileName(file.Trim());
        if (next == _selected) return;
        _selected = next;
        Changed?.Invoke();
    }

    /// <summary>Usable model files in the models folder, the default model first.</summary>
    public static IReadOnlyList<string> Models()
    {
        var dir = ModelsDirectory;
        if (dir.Length == 0 || !Directory.Exists(dir)) return [];
        return Directory.EnumerateFiles(dir, "*.gguf")
            .Where(Usable)
            .Select(path => Path.GetFileName(path))
            .Where(name => !string.Equals(name, Releasing, StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => string.Equals(name, DefaultModelFile, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Deletes one downloaded model file (a plain file name in the models folder). False when there is no such model or it cannot be deleted now.</summary>
    public static bool DeleteModel(string? file)
    {
        var name = file?.Trim() ?? "";
        if (_root.Length == 0 || name.Length == 0 || name != Path.GetFileName(name) || !name.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)) return false;
        var path = Path.Combine(ModelsDirectory, name);
        if (!File.Exists(path)) return false;
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false; // still held by the running translator
        }

        if (string.Equals(_selected, name, StringComparison.OrdinalIgnoreCase)) _selected = "";
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// Deletes the model that is in use. The translator is told to start again without it first (it holds the file open),
    /// then the file is deleted, trying again for a few seconds.
    /// </summary>
    public static void DeleteInUse()
    {
        var name = EffectiveName();
        if (name.Length == 0 || Releasing.Length > 0) return;
        Releasing = name;
        Changed?.Invoke();
        _ = Task.Run(async () =>
        {
            try
            {
                for (var attempt = 0; attempt < 12; attempt++)
                {
                    await Task.Delay(500).ConfigureAwait(false);
                    var path = Path.Combine(ModelsDirectory, name);
                    if (!File.Exists(path) || DeleteModel(name)) return;
                }
            }
            finally
            {
                Releasing = "";
                Changed?.Invoke();
            }
        });
    }

    /// <summary>The model file to run: the picked one if it is usable, else the first usable one.</summary>
    public static string? ModelPath()
    {
        var env = Environment.GetEnvironmentVariable("BPSR_QWEN_MODEL");
        if (Usable(env)) return env;
        var dir = ModelsDirectory;
        if (dir.Length == 0) return null;
        if (_selected.Length > 0 && !string.Equals(_selected, Releasing, StringComparison.OrdinalIgnoreCase))
        {
            var chosen = Path.Combine(dir, _selected);
            if (Usable(chosen)) return chosen;
        }

        var first = Models().FirstOrDefault();
        return first is null ? null : Path.Combine(dir, first);
    }

    /// <summary>
    /// A grey line under the model choice: the graphics card to run the model on the GPU (the 4B model: 8 GB of video memory,
    /// the 9B model: 12 GB), with card examples; when the free video memory is known (read while the settings window is open)
    /// it is added, and a note if the model and its work memory (about 1.5 GB) would not fit. Empty when there is no model.
    /// </summary>
    public static string GpuHint(long modelBytes, double? freeGb)
    {
        if (modelBytes <= 0) return "";
        var size = modelBytes / (1024.0 * 1024 * 1024);
        var (vram, cards) = size switch
        {
            <= 4.5 => (8, "RTX 3060 Ti · RTX 3070 · RTX 4060 Ti급"),
            <= 8 => (12, "RTX 3060 12GB · RTX 4070급"),
            <= 12 => (16, "RTX 4070 Ti Super · RTX 4080급"),
            _ => (24, "RTX 3090 · RTX 4090급"),
        };
        var text = $"GPU 권장: VRAM {vram}GB 이상 ({cards})";
        if (freeGb is null) return text;
        text += $" · 현재 여유 {freeGb.Value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}GB";
        return freeGb.Value < size + 1.5 ? text + " · 부족할 수 있음" : text;
    }

    /// <summary>The hint for the model that will run (with the free video memory while a settings window is open).</summary>
    public static string GpuHintForModel()
    {
        var path = ModelPath();
        try
        {
            return GpuHint(path is null ? 0 : new FileInfo(path).Length, GpuMemory.Current()?.FreeGb);
        }
        catch (IOException)
        {
            return "";
        }
    }

    /// <summary>File name of the model that will run ("" when none).</summary>
    public static string EffectiveName() => Path.GetFileName(ModelPath() ?? "");

    /// <summary>The llama-server program: an explicit one (environment or a file put straight into <c>llama</c>) wins, else the downloaded build for the device.</summary>
    public static string? LlamaPath(bool gpu)
    {
        var env = Environment.GetEnvironmentVariable("BPSR_LLAMA_SERVER");
        if (File.Exists(env)) return env;
        var dir = LlamaDirectory;
        if (dir.Length == 0) return null;
        var own = Path.Combine(dir, "llama-server.exe");
        if (File.Exists(own)) return own;
        var built = Path.Combine(dir, gpu ? "vulkan" : "cpu", "llama-server.exe");
        return File.Exists(built) ? built : null;
    }

    /// <summary>True when both the model and the llama-server are there.</summary>
    public static bool Ready => ModelPath() is not null && LlamaPath(false) is not null;

    /// <summary>One short line for the in-game settings window.</summary>
    public static string StatusText()
    {
        if (_root.Length == 0) return "경로 미설정";
        switch (Volatile.Read(ref _phase))
        {
            case 1:
                return $"받는 중 · {_label} {Megabytes(Interlocked.Read(ref _done))} / {Megabytes(Interlocked.Read(ref _total))} MB";
            case 2:
                return "실패 · " + _failure;
        }

        var model = ModelPath() is not null ? "있음" : "없음";
        var tools = LlamaPath(false) is not null ? "있음" : "없음";
        return $"모델 {model} · 번역 도구 {tools}";
    }

    /// <summary>Starts the download of everything missing in the background (nothing happens while one is running).</summary>
    public static void StartDownload()
    {
        lock (Gate)
        {
            if (Downloading || _root.Length == 0) return;
            Volatile.Write(ref _phase, 1);
            _label = "준비";
            _failure = "";
            Interlocked.Exchange(ref _done, 0);
            Interlocked.Exchange(ref _total, 0);
        }

        _shared ??= NewClient();
        _ = Task.Run(() => RunAsync(_shared, Pieces, CancellationToken.None));
    }

    /// <summary>Downloads what is missing and checks it. Returns false (and shows the reason in the status line) when a piece fails.</summary>
    public static async Task<bool> DownloadAsync(HttpClient client, IReadOnlyList<Piece> pieces, CancellationToken cancel = default)
    {
        lock (Gate)
        {
            if (Downloading) return false;
            Volatile.Write(ref _phase, 1);
            _failure = "";
        }

        return await RunAsync(client, pieces, cancel).ConfigureAwait(false);
    }

    static async Task<bool> RunAsync(HttpClient client, IReadOnlyList<Piece> pieces, CancellationToken cancel)
    {
        var part = "";
        try
        {
            foreach (var piece in pieces)
            {
                if (Present(piece)) continue;
                _label = piece.Label;
                Interlocked.Exchange(ref _done, 0);
                Interlocked.Exchange(ref _total, piece.Bytes);
                var target = Path.Combine(_root, piece.Relative);
                part = target + ".part";
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await Fetch(client, piece, part, cancel).ConfigureAwait(false);
                if (piece.Unzip)
                {
                    var staging = target + ".unpack";
                    if (Directory.Exists(staging)) Directory.Delete(staging, true);
                    ZipFile.ExtractToDirectory(part, staging, true);
                    if (Directory.Exists(target)) Directory.Delete(target, true);
                    Directory.Move(staging, target);
                    File.Delete(part);
                }
                else
                {
                    File.Move(part, target, true);
                }

                part = "";
            }

            Volatile.Write(ref _phase, 3);
            Changed?.Invoke();
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (part.Length > 0)
            {
                try
                {
                    File.Delete(part);
                }
                catch (IOException)
                {
                }
            }

            _failure = $"{_label}: {Brief(ex)}";
            Volatile.Write(ref _phase, 2);
            return false;
        }
    }

    static async Task Fetch(HttpClient client, Piece piece, string part, CancellationToken cancel)
    {
        using var response = await client.GetAsync(piece.Url, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using (var source = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false))
        await using (var file = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
        {
            var buffer = new byte[81920];
            int read;
            while ((read = await source.ReadAsync(buffer, cancel).ConfigureAwait(false)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                await file.WriteAsync(buffer.AsMemory(0, read), cancel).ConfigureAwait(false);
                Interlocked.Add(ref _done, read);
            }
        }

        var actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        if (!string.Equals(actual, piece.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("받은 파일이 다릅니다(해시 불일치)");
    }

    static bool Present(Piece piece)
    {
        var target = Path.Combine(_root, piece.Relative);
        return piece.Unzip
            ? File.Exists(Path.Combine(target, "llama-server.exe"))
            : File.Exists(target) && new FileInfo(target).Length >= piece.Bytes;
    }

    static bool Usable(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
        try
        {
            var length = new FileInfo(path).Length;
            return string.Equals(Path.GetFileName(path), DefaultModelFile, StringComparison.OrdinalIgnoreCase) ? length >= DefaultModelBytes : length > 0;
        }
        catch (IOException)
        {
            return false;
        }
    }

    static HttpClient NewClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan }; // the model is large; the speed is the user's connection
        client.DefaultRequestHeaders.UserAgent.ParseAdd("StarOverlay");
        return client;
    }

    static string Megabytes(long bytes) => (bytes / 1048576.0).ToString("0.00");

    static string Brief(Exception ex)
    {
        var text = ex is HttpRequestException { StatusCode: { } code } ? $"서버 응답 {(int)code}" : ex.Message;
        return text.Length > 60 ? text[..60] : text;
    }

    /// <summary>Forgets the download state and the picked model (tests start from a clean state).</summary>
    public static void Reset()
    {
        _selected = "";
        Releasing = "";
        Volatile.Write(ref _phase, 0);
        _failure = "";
        _label = "";
        Interlocked.Exchange(ref _done, 0);
        Interlocked.Exchange(ref _total, 0);
    }
}
