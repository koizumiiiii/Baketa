using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.OCR.Measurement;

/// <summary>
/// OCR精度測定用のテスト画像生成ユーティリティ（シンプル実装）
/// </summary>
public sealed class TestImageGenerator(ILogger<TestImageGenerator> logger)
{
    private readonly ILogger<TestImageGenerator> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// 基本的なダミー画像パスを生成（実際の画像生成はスキップ）
    /// </summary>
    /// <param name="text">想定するテキスト</param>
    /// <param name="outputPath">出力パス</param>
    /// <returns>画像パス（実際のファイルは存在しない）</returns>
    public async Task<string> GenerateTextImageAsync(
        string text, 
        string outputPath)
    {
        // ディレクトリが存在しない場合は作成
        var directory = System.IO.Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory) && !System.IO.Directory.Exists(directory))
        {
            System.IO.Directory.CreateDirectory(directory);
        }

        // ダミーファイルを作成（テスト用）
        await System.IO.File.WriteAllTextAsync(outputPath + ".txt", $"Expected: {text}").ConfigureAwait(false);

        _logger.LogInformation("📷 テスト画像パス生成完了: {OutputPath} - テキスト: '{Text}'", outputPath, text);
        return outputPath;
    }

    /// <summary>
    /// ゲーム画面風のダミー画像パスを生成
    /// </summary>
    /// <param name="text">想定するテキスト</param>
    /// <param name="outputPath">出力パス</param>
    /// <param name="gameStyle">ゲームスタイル</param>
    /// <returns>画像パス</returns>
    public async Task<string> GenerateGameStyleImageAsync(
        string text, 
        string outputPath, 
        GameImageStyle gameStyle = GameImageStyle.DialogBox)
    {
        var directory = System.IO.Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory) && !System.IO.Directory.Exists(directory))
        {
            System.IO.Directory.CreateDirectory(directory);
        }

        // スタイル情報を含むダミーファイル作成
        await System.IO.File.WriteAllTextAsync(outputPath + ".txt", 
            $"Expected: {text}\nStyle: {gameStyle}").ConfigureAwait(false);

        _logger.LogInformation("🎮 ゲーム風テスト画像パス生成完了: {OutputPath} - スタイル: {Style}", outputPath, gameStyle);
        return outputPath;
    }

    /// <summary>
    /// 複数のテストケース画像を一括生成
    /// </summary>
    /// <param name="testDataDir">出力ディレクトリ</param>
    /// <returns>生成されたテストケースのリスト</returns>
    public async Task<IReadOnlyList<(string ImagePath, string ExpectedText)>> GenerateTestCasesAsync(string testDataDir)
    {
        var testCases = new List<(string, string)>();

        // 基本的なテキストサンプル
        var basicTexts = new[]
        {
            ("simple_jp_1.png", "こんにちは"),
            ("simple_jp_2.png", "さようなら"),
            ("simple_en_1.png", "Hello World"),
            ("simple_en_2.png", "Goodbye"),
            ("numbers_1.png", "HP: 100/200"),
            ("numbers_2.png", "Level: 25"),
            ("mixed_1.png", "攻撃力 +15"),
            ("mixed_2.png", "Speed: 高速")
        };

        // 基本画像生成
        foreach (var (fileName, text) in basicTexts)
        {
            var imagePath = System.IO.Path.Combine(testDataDir, "basic", fileName);
            await GenerateTextImageAsync(text, imagePath).ConfigureAwait(false);
            testCases.Add((imagePath, text));
        }

        // ゲーム風画像生成
        var gameTexts = new[]
        {
            ("dialog_1.png", "勇者よ、準備はできたか？", GameImageStyle.DialogBox),
            ("dialog_2.png", "この先に危険が待っている。", GameImageStyle.DialogBox),
            ("menu_1.png", "アイテム", GameImageStyle.MenuText),
            ("menu_2.png", "装備", GameImageStyle.MenuText),
            ("status_1.png", "HP: 150", GameImageStyle.StatusText),
            ("status_2.png", "MP: 80", GameImageStyle.StatusText)
        };

        foreach (var (fileName, text, style) in gameTexts)
        {
            var imagePath = System.IO.Path.Combine(testDataDir, "game", fileName);
            await GenerateGameStyleImageAsync(text, imagePath, style).ConfigureAwait(false);
            testCases.Add((imagePath, text));
        }

        _logger.LogInformation("📦 テストケース一括生成完了: {TestCaseCount}件", testCases.Count);
        return testCases;
    }
}

/// <summary>
/// ゲーム画像のスタイル
/// </summary>
public enum GameImageStyle
{
    /// <summary>
    /// ダイアログボックス風
    /// </summary>
    DialogBox,
    
    /// <summary>
    /// メニューテキスト風
    /// </summary>
    MenuText,
    
    /// <summary>
    /// ステータステキスト風
    /// </summary>
    StatusText
}
