using System.Diagnostics;
using KuangYi.Core.Models;
using KuangYi.Core.Offline;

var root = args.Length > 0
    ? args[0]
    : Path.Combine(Environment.CurrentDirectory, "models");

Console.WriteLine("model root: " + root);
using var provider = new OfflineModelProvider(root);
Console.WriteLine("installed: " + string.Join(", ", provider.InstalledDirections));
if (!provider.IsAvailable)
{
    Console.WriteLine("no offline models found");
    return 1;
}

var cases = new (string Source, string Target, string Text)[]
{
    ("en", "zh", "Hello world, this is an offline translation test."),
    ("en", "zh", "Account"),
    ("zh", "en", "你好，世界。这是离线翻译测试。"),
    ("en", "zh", "Hello world.\n\nThis is the second paragraph.\n\nAnd a third one."),
    ("en", "ru", "Good morning. How are you?"),
    ("ru", "en", "Доброе утро. Как дела?"),
    ("en", "ja", "Good morning. Thank you."),
    ("ja", "en", "おはようございます。ありがとう。"),
    ("en", "ko", "Good morning. Thank you."),
    ("ko", "en", "안녕하세요. 감사합니다."),
    ("ja", "zh", "おはようございます。"),
    ("ru", "ko", "Доброе утро."),
    ("en", "zh", "The Importance Of Reading. Reading plays an important role in our daily life. It is not only a way to gain knowledge, but also a good habit that helps us grow. Through reading, we can learn about the world without leaving home. Books introduce us to different cultures, histories, and ideas. They help us understand people better and make us more open-minded. For students, reading is especially useful because it improves language skills, vocabulary and thinking. Besides knowledge, reading also brings happiness. When we are tired or stressed, a good book can help us relax. It allows us to escape from daily pressure and enter a new imaginative world. Many people think reading can make them calm and confident. In my view, everyone should spend some time reading every day. Even 20 minutes can make a difference. If we keep reading, we will surely become wiser and richer. Finally, reading is one of our most valuable habits. It opens the door to knowledge, beauty and a better future."),
    ("ja", "zh", "読書は私たちの日常生活の中で重要な役割を果たしています。読書は知識を得る方法であるだけでなく、私たちの成長を助ける良い習慣でもあります。読書を通じて、私たちは家を出ることなく世界について学ぶことができます。本は私たちにさまざまな文化、歴史、考え方を紹介してくれます。読書は人々をよりよく理解するのに役立ち、私たちをより広い視野に導きます。学生にとって読書は特に役に立ちます。なぜなら、言語能力、語彙、思考力を高めるからです。知識だけでなく、読書は楽しさももたらします。疲れているときやストレスを感じているとき、良い本は私たちをリラックスさせてくれます。"),
    ("ko", "zh", "독서는 우리의 일상생활에서 중요한 역할을 합니다. 독서는 지식을 얻는 방법일 뿐만 아니라 우리의 성장을 돕는 좋은 습관입니다. 독서를 통해 우리는 집을 떠나지 않고도 세계에 대해 배울 수 있습니다. 책은 우리에게 다양한 문화, 역사, 생각을 소개해 줍니다. 독서는 사람들을 더 잘 이해하는 데 도움이 되고 우리를 더 넓은 시야로 이끕니다. 학생들에게 독서는 특히 유용합니다. 언어 능력, 어휘, 사고력을 키워 주기 때문입니다. 지식뿐만 아니라 독서는 즐거움도 가져다줍니다. 피곤하거나 스트레스를 받을 때 좋은 책은 우리를 편안하게 해 줍니다."),
};

var failures = 0;
foreach (var test in cases)
{
    var direct = provider.InstalledDirections.Contains($"{test.Source}-{test.Target}");
    var pivot = provider.InstalledDirections.Contains($"{test.Source}-en")
                && provider.InstalledDirections.Contains($"en-{test.Target}");

    if (!direct && !pivot)
    {
        Console.WriteLine($"[skip] {test.Source}->{test.Target} (model missing)");
        continue;
    }

    var watch = Stopwatch.StartNew();
    try
    {
        var outcome = await provider.TranslateAsync(new TranslationRequest(test.Text, test.Source, test.Target), CancellationToken.None);
        watch.Stop();
        if (outcome is null || string.IsNullOrWhiteSpace(outcome.Text))
        {
            failures++;
            Console.WriteLine($"[fail] {test.Source}->{test.Target} empty ({watch.ElapsedMilliseconds} ms)");
            continue;
        }

        Console.WriteLine($"[ok] {test.Source}->{test.Target} {watch.ElapsedMilliseconds} ms · {outcome.ProviderName}");
        Console.WriteLine("     " + outcome.Text.Replace("\n", " "));
    }
    catch (Exception ex)
    {
        failures++;
        Console.WriteLine($"[error] {test.Source}->{test.Target}: {ex.Message}");
    }
}

Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} failures");

if (args.Length > 1 && int.TryParse(args[1], out var holdSeconds) && holdSeconds > 0)
{
    var process = Process.GetCurrentProcess();
    Console.WriteLine($"memory before hold: ws={process.WorkingSet64 / 1024 / 1024} MB private={process.PrivateMemorySize64 / 1024 / 1024} MB");
    Thread.Sleep(TimeSpan.FromSeconds(holdSeconds));
    process.Refresh();
    Console.WriteLine($"memory after hold: ws={process.WorkingSet64 / 1024 / 1024} MB private={process.PrivateMemorySize64 / 1024 / 1024} MB");
}

return failures == 0 ? 0 : 1;
