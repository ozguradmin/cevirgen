// SPDX-License-Identifier: AGPL-3.0-only

using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Cevirgen.Translation;

/// <summary>
/// Paketlenmiş Python/CTranslate2 çalışma zamanını kalıcı bir alt süreçte tutar. JSON satırları yalnız
/// yerel stdin/stdout üzerinden geçer; her altyazıda süreç veya model yeniden açılmaz.
/// </summary>
internal sealed class Ct2MarianEngine : ISentenceEngine
{
    private readonly MarianTokenizer tokenizer;
    private readonly string modelDirectory;
    private readonly int maxOutputTokens;
    private readonly int beams;
    private readonly Action? onGpuFallback;
    private readonly Task? cudaReady;
    private readonly string? cudaDirectory;
    private readonly int cpuThreads;
    private int runningThreads;
    private Process worker;
    private bool gpuActive;
    private bool gpuAttempted;
    private bool disposed;

    internal bool GpuActive => gpuActive;

    /// <summary>
    /// Çevirgen: hızlı modda CPU iş parçacığı sayısı (çekirdeklerin yarısı, en çok 8; ayardaki sayı daha büyükse o).
    /// 16 mantıksal çekirdekte 4 cümlelik satır 2 iş parçacığıyla ~260 ms, 8 ile ~140 ms (2026-11-25 ölçümü).
    /// </summary>
    internal static int FastThreads => Math.Clamp(Environment.ProcessorCount / 2, 2, 8);

    private int ThreadsFor(bool fast) => fast ? Math.Max(cpuThreads, Math.Min(FastThreads, Environment.ProcessorCount)) : cpuThreads;

    public Ct2MarianEngine(string modelDirectory, int maxOutputTokens, int beams, bool useGpu, Action? onGpuFallback,
        Task? cudaReady = null, string? cudaDirectory = null, int cpuThreads = 2)
    {
        this.modelDirectory = modelDirectory;
        this.maxOutputTokens = maxOutputTokens;
        this.beams = Math.Max(1, beams);
        this.onGpuFallback = onGpuFallback;
        this.cudaReady = useGpu ? cudaReady : null;
        this.cudaDirectory = cudaDirectory;
        this.cpuThreads = Math.Clamp(cpuThreads, 1, Math.Max(1, Environment.ProcessorCount));
        tokenizer = MarianTokenizer.Load(Path.Combine(modelDirectory, ModelStore.SourceSpmFile),
            Path.Combine(modelDirectory, ModelStore.VocabFile));
        if (useGpu && (cudaReady is null || cudaReady.IsCompletedSuccessfully))
        {
            gpuAttempted = true;
            try { worker = Start("cuda"); gpuActive = true; return; }
            catch (Exception error)
            {
                Debug.WriteLine($"CTranslate2 CUDA unavailable: {error}");
                onGpuFallback?.Invoke();
            }
        }
        worker = Start("cpu");
    }

    private Process Start(string device) => Start(device, cpuThreads);

    private Process Start(string device, int threads)
    {
        var runtime = Environment.GetEnvironmentVariable("COUCHSUBS_CT2_RUNTIME")
            ?? Path.Combine(AppContext.BaseDirectory, "ct2");
        var python = Path.Combine(runtime, "python.exe");
        var script = Path.Combine(runtime, "worker.py");
        if (!File.Exists(python) || !File.Exists(script))
            throw new FileNotFoundException("CTranslate2 runtime is missing", !File.Exists(python) ? python : script);
        var info = new ProcessStartInfo(python)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = runtime,
        };
        info.Environment["PYTHONIOENCODING"] = "utf-8";
        if (device == "cuda" && cudaDirectory is not null)
        {
            info.Environment["COUCHSUBS_CUDA_DIR"] = cudaDirectory;
            info.Environment["PATH"] = cudaDirectory + Path.PathSeparator + info.Environment["PATH"];
        }
        info.ArgumentList.Add("-u");
        info.ArgumentList.Add(script);
        info.ArgumentList.Add(modelDirectory);
        info.ArgumentList.Add(device);
        info.ArgumentList.Add((device == "cpu" ? threads : 2).ToString(System.Globalization.CultureInfo.InvariantCulture));
        var process = Cevirgen.Core.ChildProcessGuard.Start(info) ?? throw new IOException("CTranslate2 worker did not start");
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) Debug.WriteLine("CTranslate2: " + e.Data); };
        process.BeginErrorReadLine();
        try
        {
            var line = process.StandardOutput.ReadLine();
            if (line is null) throw new IOException("CTranslate2 worker exited during startup");
            using var response = JsonDocument.Parse(line);
            if (!response.RootElement.TryGetProperty("ready", out var ready) || !ready.GetBoolean())
                throw new IOException(response.RootElement.GetProperty("error").GetString());
            if (device == "cpu") runningThreads = threads;
            return process;
        }
        catch { Stop(process); throw; }
    }

    public string Translate(string sentence, CancellationToken cancellationToken) => Translate(sentence, false, cancellationToken);

    /// <summary>Çevirgen: <paramref name="fast"/> ise tek adaylı arama (beam 1) ve daha çok iş parçacığı.</summary>
    public string Translate(string sentence, bool fast, CancellationToken cancellationToken) =>
        Run(fast, cancellationToken, size => TranslateCurrent(sentence, size, cancellationToken));

    /// <summary>
    /// Çevirgen: satırın cümleleri tek istekte (CTranslate2 translate_batch). Model cümleleri birlikte işler; 4 cümlelik
    /// satır sırayla ~560 ms, birlikte ~280 ms sürüyordu.
    /// </summary>
    public IReadOnlyList<string> TranslateBatch(IReadOnlyList<string> sentences, bool fast, CancellationToken cancellationToken) =>
        sentences.Count == 1
            ? [Translate(sentences[0], fast, cancellationToken)]
            : Run(fast, cancellationToken, size => TranslateMany(sentences, size, cancellationToken));

    /// <summary>Hızlı moda geçilince iş parçacığı sayısı hemen değişsin (model ilk satırı beklemeden yeniden yüklenir).</summary>
    public void Prepare(bool fast)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        EnsureThreads(fast);
    }

    private void EnsureThreads(bool fast)
    {
        var wanted = ThreadsFor(fast);
        if (gpuActive || wanted == runningThreads) return;
        var replacement = Start("cpu", wanted);
        Stop(worker);
        worker = replacement;
    }

    private T Run<T>(bool fast, CancellationToken cancellationToken, Func<int, T> work)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (!gpuAttempted && cudaReady is { IsCompleted: true })
        {
            gpuAttempted = true;
            if (cudaReady.IsCompletedSuccessfully)
            {
                try
                {
                    var cudaWorker = Start("cuda");
                    Stop(worker);
                    worker = cudaWorker;
                    gpuActive = true;
                }
                catch (Exception error)
                {
                    Debug.WriteLine($"CTranslate2 CUDA unavailable after download: {error}");
                    onGpuFallback?.Invoke();
                }
            }
            else
            {
                Debug.WriteLine($"CUDA runtime download failed: {cudaReady.Exception}");
                onGpuFallback?.Invoke();
            }
        }
        EnsureThreads(fast);
        var size = fast ? 1 : beams;
        try { return work(size); }
        catch (Exception error) when (gpuActive && error is not OperationCanceledException)
        {
            Debug.WriteLine($"CTranslate2 CUDA translation failed: {error}");
            Stop(worker);
            gpuActive = false;
            onGpuFallback?.Invoke();
            worker = Start("cpu", ThreadsFor(fast));
            return work(size);
        }
    }

    private string[] Encode(string sentence)
    {
        // Sözlükte olmayan karakterler zararsız karşılığına (é → e, — → –); çıktıdaki ⁇ kaynaktan doldurulur.
        var tokens = tokenizer.EncodeTokens(UnknownPieces.Simplify(sentence, tokenizer.Knows));
        // Mevcut ONNX hattıyla aynı kaynak sınırı. Son belirteç EOS kalır.
        return tokens.Length > 256 ? [.. tokens.Take(255), "</s>"] : tokens;
    }

    private JsonDocument Ask(string request)
    {
        worker.StandardInput.WriteLine(request);
        worker.StandardInput.Flush();
        var line = worker.StandardOutput.ReadLine() ?? throw new IOException("CTranslate2 worker exited during translation");
        var response = JsonDocument.Parse(line);
        // Tek satırın hatası (süreç ayakta): yalnız o satır çevrilemez, motor kapatılmaz. IOException yalnız
        // sürecin kapandığını bildirir; MarianTranslator onu kalıcı motor hatası sayar.
        if (response.RootElement.TryGetProperty("error", out var error))
        {
            var message = error.GetString();
            response.Dispose();
            throw new InvalidOperationException("CTranslate2: " + message);
        }
        return response;
    }

    private string Decode(string sentence, JsonElement tokens)
    {
        var raw = tokenizer.DecodeTokens(tokens.EnumerateArray().Select(value => value.GetString() ?? ""), UnknownPieces.Marker);
        return UnknownPieces.Repair(sentence, raw);
    }

    private string TranslateCurrent(string sentence, int beams, CancellationToken cancellationToken)
    {
        var tokens = Encode(sentence);
        using var response = Ask(JsonSerializer.Serialize(new { tokens, beams, max_output = Math.Min(maxOutputTokens, 2 * tokens.Length + 16) }));
        cancellationToken.ThrowIfCancellationRequested();
        return Decode(sentence, response.RootElement.GetProperty("tokens"));
    }

    private string[] TranslateMany(IReadOnlyList<string> sentences, int beams, CancellationToken cancellationToken)
    {
        var batch = sentences.Select(Encode).ToArray();
        var longest = batch.Max(tokens => tokens.Length);
        using var response = Ask(JsonSerializer.Serialize(new { batch, beams, max_output = Math.Min(maxOutputTokens, 2 * longest + 16) }));
        var outputs = response.RootElement.GetProperty("batch");
        if (outputs.GetArrayLength() != sentences.Count) throw new InvalidOperationException("CTranslate2: batch size mismatch");
        cancellationToken.ThrowIfCancellationRequested();
        return [.. sentences.Select((sentence, i) => Decode(sentence, outputs[i]))];
    }

    private static void Stop(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            process.WaitForExit(2000);
        }
        catch (InvalidOperationException) { }
        finally { process.Dispose(); }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Stop(worker);
    }
}
