using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KuangYi.Core.Models;
using KuangYi.Core.Services;
using KuangYi.Core.Services.Translation;

namespace KuangYi.Core.Offline;

public sealed class OfflineModelProvider : ITranslationProvider, IDisposable
{
	private static readonly HashSet<string> Codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "zh", "en", "ja", "ko", "ru" };

	private readonly string _root;

	private readonly object _gate = new object();

	private readonly Dictionary<string, string> _available = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, MarianModel> _loaded = new Dictionary<string, MarianModel>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, DateTimeOffset> _lastUsed = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, int> _active = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

	private readonly Timer _idleTimer;

	private bool _disposed;

	private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(90L);

	private const int MaxCachedModels = 2;

	public string Id => "offline";

	public string Name => "离线模型";

	public bool IsAvailable => _available.Count > 0;

	public string ModelRoot => _root;

	public IReadOnlyList<string> InstalledDirections => _available.Keys.OrderBy((string k) => k).ToList();

	public OfflineModelProvider(string modelRoot)
	{
		_root = modelRoot;
		Discover();
		_idleTimer = new Timer((object? _) =>
		{
			TrimIdle();
		}, null, TimeSpan.FromSeconds(15L), TimeSpan.FromSeconds(15L));
	}

	public Task<TranslationResult?> TranslateAsync(TranslationRequest request, CancellationToken ct)
	{
		return TranslateCoreAsync(request, ct);
	}

	public static string ResolveModelRoot()
	{
		string text = Path.Combine(AppContext.BaseDirectory, "models");
		if (HasModels(text))
		{
			return text;
		}
		return AppPaths.ModelDir;
	}

	public static bool HasModels(string directory)
	{
		try
		{
			return Directory.Exists(directory) && Directory.EnumerateDirectories(directory).Any(IsModelDirectory);
		}
		catch
		{
			return false;
		}
	}

	private async Task<TranslationResult?> TranslateCoreAsync(TranslationRequest request, CancellationToken ct)
	{
		string source = request.SourceCode;
		string target = request.TargetCode;
		if (string.IsNullOrWhiteSpace(request.Text) || source == target)
		{
			return null;
		}
		if (TryGetDirection(source, target, out string directory))
		{
			string text = await TranslateWithAsync(directory, request.Text, source, target, ct).ConfigureAwait(continueOnCapturedContext: false);
			if (string.IsNullOrWhiteSpace(text))
			{
				return null;
			}
			return new TranslationResult(text, Id, "离线模型 " + source + "→" + target, IsOffline: true);
		}
		if (TryGetMultilingualDirection(source, target, out string directory2))
		{
			string text2 = await TranslateWithAsync(directory2, request.Text, source, target, ct).ConfigureAwait(continueOnCapturedContext: false);
			if (!string.IsNullOrWhiteSpace(text2))
			{
				return new TranslationResult(text2, Id, $"离线模型 {source}→{target}（多语）", IsOffline: true);
			}
		}
		if (source != "en" && target != "en" && TryGetDirection(source, "en", out string directory3) && TryGetDirection("en", target, out string secondLeg))
		{
			string text3 = await TranslateWithAsync(directory3, request.Text, source, "en", ct).ConfigureAwait(continueOnCapturedContext: false);
			if (string.IsNullOrWhiteSpace(text3))
			{
				return null;
			}
			string text4 = await TranslateWithAsync(secondLeg, text3, "en", target, ct).ConfigureAwait(continueOnCapturedContext: false);
			if (string.IsNullOrWhiteSpace(text4))
			{
				return null;
			}
			return new TranslationResult(text4, Id, $"离线模型 {source}→{target}（经英语）", IsOffline: true);
		}
		return null;
	}

	private bool TryGetMultilingualDirection(string source, string target, out string directory)
	{
		directory = string.Empty;
		bool flag;
		switch (source)
		{
		case "zh":
		case "ja":
		case "ko":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		bool flag2 = flag;
		switch (target)
		{
		case "zh":
		case "ja":
		case "ko":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		bool flag3 = flag;
		if (!flag2 || !flag3 || string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		if (!_available.TryGetValue("en-ja", out string value) || !IsM2mDirectory(value))
		{
			return false;
		}
		directory = value;
		return true;
	}

	private static bool IsM2mDirectory(string directory)
	{
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "config.json")));
			JsonElement value;
			return jsonDocument.RootElement.TryGetProperty("model_type", out value) && string.Equals(value.GetString(), "m2m_100", StringComparison.OrdinalIgnoreCase);
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
		string key = source + "-" + target;
		if (_available.TryGetValue(key, out string value))
		{
			directory = value;
			return true;
		}
		directory = string.Empty;
		return false;
	}

	private async Task<string> TranslateWithAsync(string directory, string text, string sourceLanguage, string targetLanguage, CancellationToken ct)
	{
		MarianModel marianModel = Acquire(directory);
		try
		{
			return await marianModel.TranslateAsync(text, sourceLanguage, targetLanguage, ct).ConfigureAwait(continueOnCapturedContext: false);
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
			if (!_loaded.TryGetValue(directory, out MarianModel value))
			{
				while (_loaded.Count >= 2)
				{
					string text = (from key in _loaded.Keys
						where !_active.TryGetValue(key, out var value3) || value3 == 0
						orderby _lastUsed.TryGetValue(key, out var value3) ? value3 : DateTimeOffset.MinValue
						select key).FirstOrDefault();
					if (text == null)
					{
						break;
					}
					_loaded[text].Dispose();
					_loaded.Remove(text);
					_active.Remove(text);
					_lastUsed.Remove(text);
				}
				value = MarianModel.Load(Path.Combine(_root, directory), directory);
				_loaded[directory] = value;
				_active[directory] = 0;
				_lastUsed[directory] = DateTimeOffset.UtcNow;
			}
			_active[directory] = ((!_active.TryGetValue(directory, out var value2)) ? 1 : (value2 + 1));
			_lastUsed[directory] = DateTimeOffset.UtcNow;
			return value;
		}
	}

	private void Release(string directory)
	{
		bool flag = false;
		lock (_gate)
		{
			if (_active.TryGetValue(directory, out var value) && value > 0)
			{
				_active[directory] = value - 1;
				value--;
			}
			if (value == 0 && _loaded.TryGetValue(directory, out MarianModel value2) && value2.IsMultilingual)
			{
				value2.Dispose();
				_loaded.Remove(directory);
				_active.Remove(directory);
				_lastUsed.Remove(directory);
				flag = true;
			}
		}
		if (flag)
		{
			Task.Run((Action)TrimProcessMemory);
		}
	}

	public void TrimNow()
	{
		TrimIdle(force: true);
	}

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
		}
	}

	private void TrimIdle(bool force = false)
	{
		bool flag = false;
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}
			DateTimeOffset cutoff = (force ? DateTimeOffset.MaxValue : (DateTimeOffset.UtcNow - IdleTimeout));
			foreach (string item in _loaded.Keys.Where((string dir) => (!_active.TryGetValue(dir, out var value) || value == 0) && _lastUsed.TryGetValue(dir, out var value2) && value2 < cutoff).ToList())
			{
				_loaded[item].Dispose();
				_loaded.Remove(item);
				_active.Remove(item);
				_lastUsed.Remove(item);
				flag = true;
			}
			while (_loaded.Count > 2)
			{
				string text = (from key in _loaded.Keys
					where !_active.TryGetValue(key, out var value) || value == 0
					orderby _lastUsed.TryGetValue(key, out var value) ? value : DateTimeOffset.MinValue
					select key).FirstOrDefault();
				if (text == null)
				{
					break;
				}
				_loaded[text].Dispose();
				_loaded.Remove(text);
				_active.Remove(text);
				_lastUsed.Remove(text);
				flag = true;
			}
		}
		if (flag)
		{
			TrimProcessMemory();
		}
	}

	[DllImport("psapi.dll")]
	private static extern bool EmptyWorkingSet(nint process);

	private void Discover()
	{
		_available.Clear();
		if (!Directory.Exists(_root))
		{
			return;
		}
		foreach (string item in Directory.EnumerateDirectories(_root))
		{
			string fileName = Path.GetFileName(item);
			string[] array = fileName.Split('-', StringSplitOptions.RemoveEmptyEntries);
			if (array.Length == 2 && Codes.Contains(array[0]) && Codes.Contains(array[1]) && IsModelDirectory(item))
			{
				_available[fileName] = item;
			}
		}
	}

	private static bool IsModelDirectory(string directory)
	{
		try
		{
			return (File.Exists(Path.Combine(directory, "tokenizer.json")) || (File.Exists(Path.Combine(directory, "source.spm")) && File.Exists(Path.Combine(directory, "target.spm")))) && File.Exists(Path.Combine(directory, "encoder_model_quantized.onnx")) && File.Exists(Path.Combine(directory, "decoder_model_quantized.onnx")) && File.Exists(Path.Combine(directory, "config.json"));
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
			foreach (MarianModel value in _loaded.Values)
			{
				value.Dispose();
			}
			_loaded.Clear();
		}
	}
}
