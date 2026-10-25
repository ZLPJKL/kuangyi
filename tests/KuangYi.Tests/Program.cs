using System.Text;
using KuangYi.Core.Models;
using KuangYi.Core.Services;
using KuangYi.Core.Services.Translation;

var root = Path.Combine(Environment.CurrentDirectory, "artifacts", "selftest", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);

var failures = new List<string>();
var passed = 0;

void Check(string name, bool condition, string? details = null)
{
    if (condition)
    {
        passed++;
        Console.WriteLine($"  ok   {name}");
    }
    else
    {
        failures.Add(details is null ? name : $"{name} :: {details}");
        Console.WriteLine($"  FAIL {name} {details}");
    }
}

void Section(string name) => Console.WriteLine(Environment.NewLine + "[" + name + "]");

Section("LanguageDetector");
Check("中文", LanguageDetector.Detect("今天天气不错，我们去公园散步。").Language.Code == "zh");
Check("日语", LanguageDetector.Detect("これは日本語のテストです。").Language.Code == "ja");
Check("韩语", LanguageDetector.Detect("한국어 문장을 인식합니다.").Language.Code == "ko");
Check("俄语", LanguageDetector.Detect("Это тестовое предложение на русском.").Language.Code == "ru");
Check("英语", LanguageDetector.Detect("This is an English sentence for testing.").Language.Code == "en");
var kanjiOnly = LanguageDetector.Detect("東京都");
Check("纯汉字回落中文且低置信", kanjiOnly.Language.Code == "zh" && !kanjiOnly.IsConfident);
Check("含假名判为日语", LanguageDetector.Detect("東京で会議があります").Language.Code == "ja");
Check("空文本为未知", LanguageDetector.Detect("   ").Language.Code == Languages.Auto);

Section("SettingsService");
var settingsPath = Path.Combine(root, "settings.json");
var settingsService = new SettingsService(settingsPath);
var defaults = settingsService.Load();
Check("默认热键 F2", defaults.Hotkey == "F2");
Check("默认纯白主题", defaults.Theme == "White");
Check("默认历史 50/30", defaults.HistoryLimit == 50 && defaults.HistoryDays == 30);
Check("收藏上限 50", defaults.FavoriteLimit == 50);
Check("在线默认关闭", !defaults.OnlineEnabled);

defaults.Hotkey = "Ctrl+Alt+T";
defaults.Theme = "Black";
defaults.OnlineEnabled = true;
settingsService.Save(defaults);
var reloaded = new SettingsService(settingsPath).Load();
Check("设置往返", reloaded.Hotkey == "Ctrl+Alt+T" && reloaded.Theme == "Black" && reloaded.OnlineEnabled);

var clamped = SettingsService.Normalize(new AppSettings { HistoryLimit = 99999, HistoryDays = 0, FavoriteLimit = 500 });
Check("历史上限夹取", clamped.HistoryLimit is >= 10 and <= 500);
Check("天数夹取", clamped.HistoryDays >= 1);
Check("收藏上限不超过 50", clamped.FavoriteLimit == 50);

var brokenPath = Path.Combine(root, "broken.json");
File.WriteAllText(brokenPath, "{ not json");
Check("损坏配置回落默认", new SettingsService(brokenPath).Load().Hotkey == "F2");

Section("HistoryService");
var history = new HistoryService(Path.Combine(root, "history.db"), Path.Combine(root, "thumbs"));
var options = new HistoryOptions(NonFavoriteLimit: 3, Days: 30, FavoriteLimit: 2);

for (var i = 0; i < 5; i++)
{
    await history.AddAsync(NewEntry($"text-{i}", DateTimeOffset.UtcNow.AddMinutes(i)), options);
}

var recent = await history.GetRecentAsync();
Check("超出上限淘汰最旧", recent.Count == 3 && recent[0].SourceText == "text-4", string.Join(",", recent.Select(e => e.SourceText)));

await history.AddAsync(NewEntry("stale", DateTimeOffset.UtcNow.AddDays(-31)), options);
recent = await history.GetRecentAsync();
Check("过期条目被清理", recent.All(e => e.SourceText != "stale"));

var keepId = await history.AddAsync(NewEntry("keep-me", DateTimeOffset.UtcNow.AddMinutes(10)), options);
Check("收藏成功", await history.ToggleFavoriteAsync(keepId, options) == FavoriteToggleResult.Ok);
for (var i = 0; i < 4; i++)
{
    await history.AddAsync(NewEntry($"plain-{i}", DateTimeOffset.UtcNow.AddMinutes(i)), options);
}

var favorites = await history.GetFavoritesAsync();
Check("收藏不受最近上限淘汰", favorites.Count == 1 && favorites[0].SourceText == "keep-me");

var favA = await history.AddAsync(NewEntry("fav-a", DateTimeOffset.UtcNow.AddMinutes(20)), options);
var favB = await history.AddAsync(NewEntry("fav-b", DateTimeOffset.UtcNow.AddMinutes(21)), options);
Check("收藏 A 成功", await history.ToggleFavoriteAsync(favA, options) == FavoriteToggleResult.Ok);
Check("收藏上限触发", await history.ToggleFavoriteAsync(favB, options) == FavoriteToggleResult.LimitReached);

await history.ToggleFavoriteAsync(keepId, options);
recent = await history.GetRecentAsync();
Check("取消收藏回到最近", recent.Any(e => e.SourceText == "keep-me"));

await history.AddAsync(new HistoryEntry
{
    SourceText = "good morning",
    TargetText = "早上好",
    SourceLang = "en",
    TargetLang = "zh",
    Provider = "selftest",
    CreatedAt = DateTimeOffset.UtcNow.AddMinutes(30),
}, options);
Check("按译文搜索", (await history.SearchAsync("早上", false)).Count == 1);
Check("按原文搜索（大小写不敏感）", (await history.SearchAsync("GOOD", false)).Count == 1);

var exportPath = Path.Combine(root, "export.csv");
var exported = await history.ExportCsvAsync(exportPath, favoritesOnly: false);
Check("导出 CSV", exported > 0 && File.ReadAllText(exportPath).Contains("good morning"));

await history.ClearAsync(includeFavorites: false);
var stats = await history.GetStatsAsync();
Check("清空最近保留收藏", stats.RecentCount == 0 && stats.FavoriteCount == 1, $"recent={stats.RecentCount} fav={stats.FavoriteCount}");
await history.ClearAsync(includeFavorites: true);
stats = await history.GetStatsAsync();
Check("清空全部", stats.Total == 0);

Section("History thumbnails");
var thumbDir = Path.Combine(root, "thumbs2");
var history2 = new HistoryService(Path.Combine(root, "history2.db"), thumbDir);
var tight = new HistoryOptions(NonFavoriteLimit: 1, Days: 30, FavoriteLimit: 50);
var thumbFile = Path.Combine(thumbDir, "thumb.png");
await File.WriteAllBytesAsync(thumbFile, new byte[] { 1, 2, 3 });
await history2.AddAsync(new HistoryEntry
{
    SourceText = "with-thumb",
    TargetText = "带图",
    SourceLang = "en",
    TargetLang = "zh",
    Provider = "selftest",
    ThumbPath = thumbFile,
    CreatedAt = DateTimeOffset.UtcNow,
}, tight);
await history2.AddAsync(NewEntry("newer", DateTimeOffset.UtcNow.AddMinutes(1)), tight);
Check("淘汰时删除缩略图文件", !File.Exists(thumbFile));

Section("TranslationPipeline");
var pipeline = new TranslationPipeline(new ITranslationProvider[]
{
    new FakeProvider("bad", "坏源", _ => throw new InvalidOperationException("boom")),
    new FakeProvider("good", "好源", _ => new TranslationResult("你好", "good", "好源", false)),
});
var outcome = await pipeline.TranslateAsync(new TranslationRequest("hello", "en", "zh"));
Check("失败后降级到下一源", outcome.Success && outcome.Result!.Text == "你好" && outcome.Attempts.Count == 2);
Check("首个来源记录为失败", !outcome.Attempts[0].Success);

var allBad = new TranslationPipeline(new ITranslationProvider[]
{
    new FakeProvider("a", "甲", _ => throw new InvalidOperationException("网络错误")),
    new FakeProvider("b", "乙", _ => null),
});
var failed = await allBad.TranslateAsync(new TranslationRequest("hello", "en", "zh"));
Check("全部失败给出汇总", !failed.Success && failed.SummaryError!.Contains("网络错误") && failed.SummaryError.Contains("未返回结果"));

var same = await new TranslationPipeline(Array.Empty<ITranslationProvider>())
    .TranslateAsync(new TranslationRequest("hello", "en", "en"));
Check("同语言直接返回", same.Success && same.Result!.Text == "hello");

var blank = await new TranslationPipeline(Array.Empty<ITranslationProvider>())
    .TranslateAsync(new TranslationRequest("   ", "en", "zh"));
Check("空文本拒绝", !blank.Success);

Section("TextChunker");
var longText = string.Concat(Enumerable.Repeat("这是一句用于测试的中文。", 60));
var chunks = TextChunker.SplitByUtf8Bytes(longText, 450);
Check("长文本分块", chunks.Count > 1);
Check("每块不超过 450 字节", chunks.All(c => Encoding.UTF8.GetByteCount(c) <= 450));
Check("分块后内容无损", string.Concat(chunks) == longText);

Console.WriteLine();
Console.WriteLine($"passed={passed} failed={failures.Count}");
if (failures.Count > 0)
{
    foreach (var failure in failures)
    {
        Console.WriteLine("FAILED: " + failure);
    }

    return 1;
}

return 0;

static HistoryEntry NewEntry(string text, DateTimeOffset createdAt) => new()
{
    SourceText = text,
    TargetText = "译-" + text,
    SourceLang = "en",
    TargetLang = "zh",
    Provider = "selftest",
    CreatedAt = createdAt,
};

internal sealed class FakeProvider : ITranslationProvider
{
    private readonly Func<TranslationRequest, TranslationResult?> _handler;

    public FakeProvider(string id, string name, Func<TranslationRequest, TranslationResult?> handler)
    {
        Id = id;
        Name = name;
        _handler = handler;
    }

    public string Id { get; }

    public string Name { get; }

    public Task<TranslationResult?> TranslateAsync(TranslationRequest request, CancellationToken ct)
        => Task.FromResult(_handler(request));
}
