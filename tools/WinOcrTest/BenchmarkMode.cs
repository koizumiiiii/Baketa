using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Text;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace WinOcrTest;

/// <summary>
/// Windows OCR 多言語ベンチマーク
/// 合成画像を生成し、Windows OCRの認識精度をCERで自動評価
/// </summary>
public static class BenchmarkMode
{
    // テスト条件
    private static readonly (string Name, Color Bg, Color Fg)[] BackgroundStyles =
    [
        ("DarkTranslucent", Color.FromArgb(220, 10, 15, 30), Color.FromArgb(255, 232, 234, 240)),  // ゲームダイアログ風
        ("DarkSolid", Color.FromArgb(255, 20, 20, 30), Color.White),                               // 完全不透明暗背景
        ("WhiteBg", Color.White, Color.Black),                                                       // 標準（白背景黒文字）
        ("GrayBg", Color.FromArgb(255, 200, 200, 200), Color.FromArgb(255, 30, 30, 30)),           // グレー背景
        ("GameHUD", Color.FromArgb(180, 0, 0, 0), Color.FromArgb(255, 255, 220, 100)),             // ゲームHUD風（暗背景+黄色文字）
        ("BlueBox", Color.FromArgb(230, 20, 40, 80), Color.FromArgb(255, 200, 220, 255)),          // RPG会話ウィンドウ風
    ];

    private static readonly int[] FontSizes = [14, 20, 28, 36];

    // 各言語のテストテキスト（短文・中文・記号含み）
    private static readonly Dictionary<string, (string Tag, string[] Texts)> LanguageTests = new()
    {
        ["en"] = ("en-US", [
            "The quest begins at dawn.",
            "Press START to continue.",
            "You found a Legendary Sword!",
            "HP: 120/300  MP: 45/80"
        ]),
        ["ja"] = ("ja", [
            "冒険は夜明けと共に始まる。",
            "スタートボタンを押してください。",
            "伝説の剣を手に入れた！",
            "体力: 120/300  魔力: 45/80"
        ]),
        ["zh-Hans"] = ("zh-Hans-CN", [
            "冒险从黎明开始。",
            "按开始键继续。",
            "你找到了传说中的剑！",
            "生命值: 120/300  魔法值: 45/80"
        ]),
        ["zh-Hant"] = ("zh-Hant-TW", [
            "冒險從黎明開始。",
            "按開始鍵繼續。",
            "你找到了傳說中的劍！",
            "生命值: 120/300  魔法值: 45/80"
        ]),
        ["ko"] = ("ko", [
            "모험은 새벽과 함께 시작된다.",
            "시작 버튼을 눌러주세요.",
            "전설의 검을 찾았다!",
            "체력: 120/300  마력: 45/80"
        ]),
        ["de"] = ("de-DE", [
            "Das Abenteuer beginnt bei Tagesanbruch.",
            "Drücke START zum Fortfahren.",
            "Du hast ein Legendäres Schwert gefunden!",
            "HP: 120/300  MP: 45/80"
        ]),
        ["fr"] = ("fr-FR", [
            "L'aventure commence à l'aube.",
            "Appuyez sur DÉMARRER pour continuer.",
            "Vous avez trouvé une Épée Légendaire !",
            "PV: 120/300  PM: 45/80"
        ]),
        ["es"] = ("es-ES", [
            "La aventura comienza al amanecer.",
            "Pulsa INICIO para continuar.",
            "¡Encontraste una Espada Legendaria!",
            "PV: 120/300  PM: 45/80"
        ]),
        ["it"] = ("it-IT", [
            "L'avventura inizia all'alba.",
            "Premi START per continuare.",
            "Hai trovato una Spada Leggendaria!",
            "PV: 120/300  PM: 45/80"
        ]),
        ["pt"] = ("pt-PT", [
            "A aventura começa ao amanhecer.",
            "Pressione INICIAR para continuar.",
            "Você encontrou uma Espada Lendária!",
            "PV: 120/300  PM: 45/80"
        ]),
    };

    public static async Task RunAsync(string outputDir, int scaleFactor = 2, int paddingPx = 15)
    {
        Directory.CreateDirectory(outputDir);
        var csvPath = Path.Combine(outputDir, "benchmark_results.csv");
        var sb = new StringBuilder();
        sb.AppendLine("Language,OcrTag,Background,FontSize,TextIndex,ExpectedText,OcrResult,CER,TimeMs");

        var totalTests = 0;
        var totalPass = 0;

        foreach (var (langKey, (ocrTag, texts)) in LanguageTests)
        {
            // 言語パックチェック
            var language = new Windows.Globalization.Language(ocrTag);
            if (!OcrEngine.IsLanguageSupported(language))
            {
                Console.WriteLine($"  SKIP {langKey} ({ocrTag}): 言語パック未インストール");
                continue;
            }

            var engine = OcrEngine.TryCreateFromLanguage(language);
            if (engine is null)
            {
                Console.WriteLine($"  SKIP {langKey} ({ocrTag}): OCRエンジン作成失敗");
                continue;
            }

            Console.WriteLine($"\n=== {langKey} ({ocrTag}) ===");

            foreach (var (bgName, bgColor, fgColor) in BackgroundStyles)
            {
                foreach (var fontSize in FontSizes)
                {
                    for (int ti = 0; ti < texts.Length; ti++)
                    {
                        var text = texts[ti];
                        totalTests++;

                        // 画像生成
                        using var image = GenerateTextImage(text, fontSize, bgColor, fgColor, langKey, paddingPx);

                        // スケーリング
                        using var scaled = ScaleImage(image, scaleFactor);

                        // OCR実行
                        var (ocrText, timeMs) = await RunOcrAsync(engine, scaled);

                        // CER計算
                        var cer = CalculateCER(text, ocrText);
                        var pass = cer < 0.1; // 10%未満なら合格
                        if (pass) totalPass++;

                        var mark = pass ? "OK" : "NG";
                        Console.WriteLine($"  [{mark}] {bgName} f{fontSize} t{ti}: CER={cer:F3} ({timeMs}ms) \"{Truncate(ocrText, 40)}\"");

                        sb.AppendLine($"{langKey},{ocrTag},{bgName},{fontSize},{ti},\"{Escape(text)}\",\"{Escape(ocrText)}\",{cer:F4},{timeMs}");
                    }
                }
            }
        }

        await File.WriteAllTextAsync(csvPath, sb.ToString(), Encoding.UTF8);
        Console.WriteLine($"\n=== ベンチマーク完了 ===");
        Console.WriteLine($"合計: {totalTests}テスト, 合格: {totalPass} ({(totalTests > 0 ? 100.0 * totalPass / totalTests : 0):F1}%)");
        Console.WriteLine($"結果: {csvPath}");
    }

    private static Bitmap GenerateTextImage(string text, int fontSize, Color bgColor, Color fgColor, string lang, int padding)
    {
        // フォント選択（言語別）
        var fontFamily = lang switch
        {
            "ja" => "Noto Sans JP",
            "ko" => "Malgun Gothic",
            "zh-Hans" or "zh-Hant" => "Microsoft YaHei",
            _ => "Segoe UI"
        };

        // まず仮描画でテキストサイズを測定
        using var tempBmp = new Bitmap(1, 1);
        using var tempG = Graphics.FromImage(tempBmp);
        using var font = new Font(fontFamily, fontSize, FontStyle.Regular, GraphicsUnit.Pixel);
        var textSize = tempG.MeasureString(text, font);

        var width = (int)textSize.Width + padding * 2 + 20;
        var height = (int)textSize.Height + padding * 2 + 10;

        var bmp = new Bitmap(width, height);
        using var g = Graphics.FromImage(bmp);
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(bgColor);

        using var brush = new SolidBrush(fgColor);
        g.DrawString(text, font, brush, padding + 10, padding + 5);

        return bmp;
    }

    private static Bitmap ScaleImage(Bitmap source, int factor)
    {
        if (factor <= 1) return (Bitmap)source.Clone();

        var scaled = new Bitmap(source.Width * factor, source.Height * factor);
        using var g = Graphics.FromImage(scaled);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.DrawImage(source, 0, 0, scaled.Width, scaled.Height);
        return scaled;
    }

    private static async Task<(string Text, long TimeMs)> RunOcrAsync(OcrEngine engine, Bitmap image)
    {
        using var ms = new MemoryStream();
        image.Save(ms, ImageFormat.Png);
        ms.Position = 0;

        var randomAccessStream = new InMemoryRandomAccessStream();
        var outputStream = randomAccessStream.GetOutputStreamAt(0);
        var writer = new DataWriter(outputStream);
        writer.WriteBytes(ms.ToArray());
        await writer.StoreAsync();
        await outputStream.FlushAsync();
        randomAccessStream.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(randomAccessStream);
        var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await engine.RecognizeAsync(softwareBitmap);
        sw.Stop();

        return (result.Text.Trim(), sw.ElapsedMilliseconds);
    }

    /// <summary>
    /// Character Error Rate (CER) を計算
    /// レーベンシュタイン距離 / 正解テキスト長
    /// </summary>
    public static float CalculateCER(string expected, string actual)
    {
        // 日本語のWindows OCR文字間スペースを除去して比較
        var normalizedActual = actual.Replace(" ", "");
        var normalizedExpected = expected.Replace(" ", "");

        if (normalizedExpected.Length == 0)
            return normalizedActual.Length == 0 ? 0f : 1f;

        var distance = LevenshteinDistance(normalizedExpected, normalizedActual);
        return (float)distance / normalizedExpected.Length;
    }

    private static int LevenshteinDistance(string s, string t)
    {
        var n = s.Length;
        var m = t.Length;
        var d = new int[n + 1, m + 1];

        for (int i = 0; i <= n; i++) d[i, 0] = i;
        for (int j = 0; j <= m; j++) d[0, j] = j;

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                var cost = s[i - 1] == t[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[n, m];
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "...";

    private static string Escape(string s) =>
        s.Replace("\"", "\"\"");
}
