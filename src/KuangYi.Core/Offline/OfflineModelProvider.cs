using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using KuangYi.Core.Models;
using KuangYi.Core.Services;
using KuangYi.Core.Services.Translation;

namespace KuangYi.Core.Offline;

/// <summary>
/// Offline OPUS-MT provider. Direct pairs are preferred; otherwise the request is routed
/// through English when both pivot legs are installed.
/// </summary>
public sealed class OfflineModelProvider : ITranslationProvider, IDisposable
{
    private static readonly HashSet<string> Codes = new(StringComparer.OrdinalIgnoreCase)
    {
        "zh", "en", "ja", "ko", "ru",
    };

    private readonly string _root;
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _available = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MarianModel> _loaded = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _lastUsed = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _active = new(StringComparer.OrdinalIgnoreCase);
    private readonly Timer _idleTimer;
    private bool _disposed;

    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(90);
    private const int MaxCachedModels = 2;

    public OfflineModelProvider(string modelRoot)
    {
        _root = modelRoot;
        Discover();
        _idleTimer = new Timer(_ => TrimIdle(), null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));
    }

    public string Id => "offline";

    public string Name => "离线模型";

    public bool IsAvailable => _available.Count > 0;

    public string ModelRoot => _root;

    public IReadOnlyList<string> InstalledDirections => _available.Keys.OrderBy(k => k).ToList();

    public Task<TranslationResult?> TranslateAsync(TranslationRequest request, CancellationToken ct)
        => TranslateCoreAsync(request, ct);

    public static string ResolveModelRoot()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "models");
        if (HasModels(bundled))
        {
            return bundled;
        }

        return AppPaths.ModelDir;
    }

    public static bool HasModels(string directory)
    {
        try
        {
            return Directory.Exists(directory)
                   && Directory.EnumerateDirectories(directory).Any(IsModelDirectory);
        }
        catch
        {
            return false;
        }
    }

    private async Task<TranslationResult?> TranslateCoreAsync(TranslationRequest request, CancellationToken ct)
    {
        var source = request.SourceCode;
        var target = request.TargetCode;

        if (string.IsNullOrWhiteSpace(request.Text) || source == target)
        {
            return null;
        }

        if (TryGetDirection(source, target, out var direct))
        {
            var text = await TranslateWithAsync(direct, request.Text, source, target, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            return new TranslationResult(text, Id, $"离线模型 {source}→{target}", IsOffline: true);
        }

        if (TryGetMultilingualDirection(source, target, out var multilingual))
        {
            var text = await TranslateWithAsync(multilingual, request.Text, source, target, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return new TranslationResult(text, Id, $"离线模型 {source}→{target}（多语）", IsOffline: true);
            }
        }

        if (source != "en"
            && target != "en"
            && TryGetDirection(source, "en", out var firstLeg)
            && TryGetDirection("en", target, out var secondLeg))
        {
            var intermediate = await TranslateWithAsync(firstLeg, request.Text, source, "en", ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(intermediate))
            {
                return null;
            }

            var text = await TranslateWithAsync(secondLeg, intermediate, "en", target, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            return new TranslationResult(text, Id, $"离线模型 {source}→{target}（经英语）", IsOffline: true);
        }

        return null;
    }

    private bool TryGetMultilingualDirection(string source, string target, out string directory)
    {
        directory = string.Empty;

        var cjkSource = source is "zh" or "ja" or "ko";
        var cjkTarget = target is "zh" or "ja" or "ko";
        if (!cjkSource || !cjkTarget || string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!_available.TryGetValue("en-ja", out var m2m) || !IsM2mDirectory(m2m))
        {
            return false;
        }

        directory = m2m;
        return true;
    }

    private static bool IsM2mDirectory(string directory)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "config.json")));
            return document.RootElement.TryGetProperty("model_type", out var type)
                   && string.Equals(type.GetString(), "m2m_100", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private bool TryGetDirection(string source, string target, out string directory)
    {
        directory = string.Empty;
        if (!Codes.Contains(source) || !Codes.Contains(target))
        {
            return false;
        }

        var key = $"{source}-{target}";
        if (_available.TryGetValue(key, out var found))
        {
            directory = found;
            return true;
        }

        directory = string.Empty;
        return false;
    }

    private async Task<string> TranslateWithAsync(
        string directory,
        string text,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken ct)
    {
        var model = Acquire(directory);
        try
        {
            return await model.TranslateAsync(text, sourceLanguage, targetLanguage, ct).ConfigureAwait(false);
        }
        finally
        {
            Release(directory);
            TrimIdle();
        }
    }

    private MarianModel Acquire(string directory)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (!_loaded.TryGetValue(directory, out var model))
            {
                while (_loaded.Count >= MaxCachedModels)
                {
                    var victim = _loaded.Keys
                        .Where(key => !_active.TryGetValue(key, out var active) || active == 0)
                        .OrderBy(key => _lastUsed.TryGetValue(key, out var used) ? used : DateTimeOffset.MinValue)
                        .FirstOrDefault();

                    if (victim is null)
                    {
                        break;
                    }

                    _loaded[victim].Dispose();
                    _loaded.Remove(victim);
                    _active.Remove(victim);
                    _lastUsed.Remove(victim);
                }

                model = MarianModel.Load(Path.Combine(_root, directory), directory);
                _loaded[directory] = model;
                _active[directory] = 0;
                _lastUsed[directory] = DateTimeOffset.UtcNow;
            }

            _active[directory] = _active.TryGetValue(directory, out var count) ? count + 1 : 1;
            _lastUsed[directory] = DateTimeOffset.UtcNow;
            return model;
        }
    }

    private void Release(string directory)
    {
        var removedLargeModel = false;

        lock (_gate)
        {
            if (_active.TryGetValue(directory, out var count) && count > 0)
            {
                _active[directory] = count - 1;
                count--;
            }

            // The 600 MB multilingual model dominates memory. Do not keep it warm
            // between translations; reloading it is cheaper than holding it idle.
            if (count == 0
                && _loaded.TryGetValue(directory, out var loaded)
                && loaded.IsMultilingual)
            {
                loaded.Dispose();
                _loaded.Remove(directory);
                _active.Remove(directory);
                _lastUsed.Remove(directory);
                removedLargeModel = true;
            }
        }

        if (removedLargeModel)
        {
            // Never make the user wait for GC/working-set trimming after a translation.
            _ = Task.Run(TrimProcessMemory);
        }
    }

    public void TrimNow() => TrimIdle(force: true);

    private void TrimProcessMemory()
    {
        try
        {
            GC.Collect(2, GCCollectionMode.Optimized, blocking: false, compacting: false);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Optimized, blocking: false, compacting: false);
            EmptyWorkingSet(Process.GetCurrentProcess().Handle);
        }
        catch
        {
            // trimming is best effort
        }
    }

    private void TrimIdle(bool force = false)
    {
        var removed = false;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var cutoff = force ? DateTimeOffset.MaxValue : DateTimeOffset.UtcNow - IdleTimeout;
            var idle = _loaded.Keys
                .Where(dir => (!_active.TryGetValue(dir, out var active) || active == 0)
                              && _lastUsed.TryGetValue(dir, out var used)
                              && used < cutoff)
                .ToList();

            foreach (var directory in idle)
            {
                _loaded[directory].Dispose();
                _loaded.Remove(directory);
                _active.Remove(directory);
                _lastUsed.Remove(directory);
                removed = true;
            }

            while (_loaded.Count > MaxCachedModels)
            {
                var victim = _loaded.Keys
                    .Where(key => !_active.TryGetValue(key, out var active) || active == 0)
                    .OrderBy(key => _lastUsed.TryGetValue(key, out var used) ? used : DateTimeOffset.MinValue)
                    .FirstOrDefault();

                if (victim is null)
                {
                    break;
                }

                _loaded[victim].Dispose();
                _loaded.Remove(victim);
                _active.Remove(victim);
                _lastUsed.Remove(victim);
                removed = true;
            }
        }

        if (!removed)
        {
            return;
        }

        TrimProcessMemory();
    }

    [DllImport("psapi.dll")]
    private static extern bool EmptyWorkingSet(IntPtr process);

    private void Discover()
    {
        _available.Clear();
        if (!Directory.Exists(_root))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(_root))
        {
            var name = Path.GetFileName(directory);
            var parts = name.Split('-', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2
                || !Codes.Contains(parts[0])
                || !Codes.Contains(parts[1])
                || !IsModelDirectory(directory))
            {
                continue;
            }

            _available[name] = directory;
        }
    }

    private static bool IsModelDirectory(string directory)
    {
        try
        {
            var hasTokenizer = File.Exists(Path.Combine(directory, "tokenizer.json"))
                               || (File.Exists(Path.Combine(directory, "source.spm"))
                                   && File.Exists(Path.Combine(directory, "target.spm")));

            return hasTokenizer
                   && File.Exists(Path.Combine(directory, "encoder_model_quantized.onnx"))
                   && File.Exists(Path.Combine(directory, "decoder_model_quantized.onnx"))
                   && File.Exists(Path.Combine(directory, "config.json"));
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _idleTimer.Dispose();

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var model in _loaded.Values)
            {
                model.Dispose();
            }

            _loaded.Clear();
        }
    }
}
