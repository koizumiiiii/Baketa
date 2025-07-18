using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Infrastructure.Imaging;

namespace Baketa.Infrastructure.OCR.Benchmarking;

/// <summary>
/// OCRテスト用のテストケース生成クラス
/// </summary>
public class TestCaseGenerator
{
    private readonly ILogger<TestCaseGenerator> _logger;
    
    public TestCaseGenerator(ILogger<TestCaseGenerator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    
    /// <summary>
    /// 日本語・英語混在テキストのテストケースを生成
    /// </summary>
    public async Task<IEnumerable<TestCase>> GenerateJapaneseMixedTextTestCasesAsync()
    {
        _logger.LogInformation("日本語・英語混在テキストのテストケース生成開始");
        
        var testTexts = new[]
        {
            // 実際の問題として報告されたテキスト
            "オンボーディング（魔法体験）の設計",
            "単体テスト",
            "EXPLAIN でボトルネック確認",
            
            // 追加のテストケース
            "データベース接続エラー",
            "API応答時間の最適化",
            "ユーザー認証システム",
            "クラウドインフラ構築",
            "レスポンシブデザイン対応",
            "機械学習モデル訓練",
            "自動テスト実行",
            "パフォーマンス監視",
            "セキュリティ脆弱性検査",
            "コードレビュー自動化",
            "継続的インテグレーション",
            "マイクロサービス設計",
            
            // 漢字認識が難しいケース
            "複雑な漢字: 鬱陶しい",
            "似ている漢字: 未末・人八",
            "縦書き対応: 日本語縦書き",
            "英数字混在: Version 2.1.0",
            "記号混在: @username#hashtag",
            "括弧混在: 【重要】(注意)",
            
            // 小さい文字のテストケース
            "小さい文字テスト",
            "Tiny text test",
            "极小文字测试",
            
            // 複雑なレイアウト
            "複数行\nテキスト\nテスト",
            "複数列 | 区切り | テスト",
            "タブ区切り\tテスト\tデータ"
        };
        
        var testCases = new List<TestCase>();
        
        foreach (var text in testTexts)
        {
            try
            {
                // 各テキストに対して複数のフォントサイズで画像を生成
                var fontSizes = new[] { 12, 16, 20, 24 };
                
                foreach (var fontSize in fontSizes)
                {
                    var testName = $"{text}_{fontSize}px";
                    var image = await GenerateTextImageAsync(text, fontSize);
                    
                    var testCase = new TestCase(testName, image, text);
                    testCases.Add(testCase);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "テストケース生成エラー: {Text}", text);
            }
        }
        
        _logger.LogInformation("テストケース生成完了: {Count}件", testCases.Count);
        return testCases;
    }
    
    /// <summary>
    /// 特定の誤認識パターンをテストするテストケースを生成
    /// </summary>
    public async Task<IEnumerable<TestCase>> GenerateErrorPatternTestCasesAsync()
    {
        _logger.LogInformation("誤認識パターンテストケース生成開始");
        
        // 実際に報告された誤認識パターン
        var errorPatterns = new[]
        {
            ("単体テスト", "車体テスト"),
            ("オンボーディング（魔法体験）の設計", "オンボーデイシグ (院法体勝)の恐計"),
            ("役計", "設計"),
            ("恐計", "設計"),
            ("院法", "魔法"),
            ("体勝", "体験"),
            ("恐計", "設計"),
            ("車体", "単体"),
            ("デイシグ", "ディング"),
            ("勝", "験"),
            ("役", "設"),
            ("恐", "設"),
            ("院", "魔"),
            ("勝", "験"),
            ("体", "体"),
            ("恐", "設"),
            ("計", "計")
        };
        
        var testCases = new List<TestCase>();
        
        foreach (var (correctText, _) in errorPatterns)
        {
            try
            {
                var testName = $"ErrorPattern_{correctText}";
                var image = await GenerateTextImageAsync(correctText, 16);
                
                var testCase = new TestCase(testName, image, correctText);
                testCases.Add(testCase);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "誤認識パターンテストケース生成エラー: {Text}", correctText);
            }
        }
        
        _logger.LogInformation("誤認識パターンテストケース生成完了: {Count}件", testCases.Count);
        return testCases;
    }
    
    /// <summary>
    /// テキストから画像を生成（プレースホルダー実装）
    /// </summary>
    private async Task<IImage> GenerateTextImageAsync(string text, int fontSize)
    {
        return await Task.Run(() =>
        {
            // プレースホルダー実装: 実際の画像生成の代わりに
            // テキストメタデータを保持するダミー画像を作成
            var imageData = System.Text.Encoding.UTF8.GetBytes($"PlaceholderImage:{text}:{fontSize}");
            
            // 最低限の画像サイズを計算
            var width = Math.Max(200, text.Length * fontSize / 2);
            var height = Math.Max(50, fontSize + 20);
            
            return new PlaceholderImageWithSize(imageData, width, height);
        });
    }
    
    /// <summary>
    /// 実際のゲーム画面のスクリーンショットからテストケースを生成
    /// </summary>
    public async Task<IEnumerable<TestCase>> GenerateFromScreenshotsAsync(string screenshotDirectory)
    {
        _logger.LogInformation("スクリーンショットからテストケース生成開始: {Directory}", screenshotDirectory);
        
        var testCases = new List<TestCase>();
        
        if (!Directory.Exists(screenshotDirectory))
        {
            _logger.LogWarning("スクリーンショットディレクトリが存在しません: {Directory}", screenshotDirectory);
            return testCases;
        }
        
        var imageFiles = Directory.GetFiles(screenshotDirectory, "*.png")
            .Concat(Directory.GetFiles(screenshotDirectory, "*.jpg"))
            .Concat(Directory.GetFiles(screenshotDirectory, "*.jpeg"))
            .ToArray();
        
        foreach (var imageFile in imageFiles)
        {
            try
            {
                var fileName = Path.GetFileNameWithoutExtension(imageFile);
                var imageBytes = await File.ReadAllBytesAsync(imageFile);
                var image = new BitmapImage(imageBytes);
                
                // ファイル名からテキストを抽出（例：expected_text_単体テスト.png）
                var expectedText = ExtractExpectedTextFromFilename(fileName);
                
                var testCase = new TestCase($"Screenshot_{fileName}", image, expectedText);
                testCases.Add(testCase);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "スクリーンショットテストケース生成エラー: {File}", imageFile);
            }
        }
        
        _logger.LogInformation("スクリーンショットテストケース生成完了: {Count}件", testCases.Count);
        return testCases;
    }
    
    /// <summary>
    /// ファイル名から期待テキストを抽出
    /// </summary>
    private string ExtractExpectedTextFromFilename(string filename)
    {
        // ファイル名の形式: expected_text_実際のテキスト.png
        var parts = filename.Split('_');
        if (parts.Length > 2 && parts[0] == "expected" && parts[1] == "text")
        {
            return string.Join("_", parts.Skip(2));
        }
        
        // デフォルトでファイル名をそのまま使用
        return filename;
    }
}

/// <summary>
/// バイト配列から作成されるビットマップ画像
/// </summary>
public class BitmapImage : IImage
{
    private readonly byte[] _imageBytes;
    private readonly int _width;
    private readonly int _height;
    
    public BitmapImage(byte[] imageBytes)
    {
        _imageBytes = imageBytes ?? throw new ArgumentNullException(nameof(imageBytes));
        
        // 実際の画像解析は省略し、デフォルト値を使用
        _width = 800;
        _height = 100;
    }
    
    public int Width => _width;
    public int Height => _height;
    public ImageFormat Format => ImageFormat.Png;
    
    public Task<byte[]> ToByteArrayAsync()
    {
        return Task.FromResult(_imageBytes);
    }
    
    public IImage Clone()
    {
        var clonedBytes = new byte[_imageBytes.Length];
        Array.Copy(_imageBytes, clonedBytes, _imageBytes.Length);
        return new BitmapImage(clonedBytes);
    }
    
    public Task<IImage> ResizeAsync(int width, int height)
    {
        // プレースホルダー実装：リサイズ処理
        var resizedImage = new BitmapImage(_imageBytes);
        return Task.FromResult<IImage>(resizedImage);
    }
    
    public void Dispose()
    {
        // バイト配列は自動的にガベージコレクションされるため、特別な処理は不要
    }
}

/// <summary>
/// サイズ指定可能なプレースホルダー画像
/// </summary>
public class PlaceholderImageWithSize : IImage
{
    private readonly byte[] _imageBytes;
    private readonly int _width;
    private readonly int _height;
    
    public PlaceholderImageWithSize(byte[] imageBytes, int width, int height)
    {
        _imageBytes = imageBytes ?? throw new ArgumentNullException(nameof(imageBytes));
        _width = width;
        _height = height;
    }
    
    public int Width => _width;
    public int Height => _height;
    public ImageFormat Format => ImageFormat.Png;
    
    public Task<byte[]> ToByteArrayAsync()
    {
        return Task.FromResult(_imageBytes);
    }
    
    public IImage Clone()
    {
        var clonedBytes = new byte[_imageBytes.Length];
        Array.Copy(_imageBytes, clonedBytes, _imageBytes.Length);
        return new PlaceholderImageWithSize(clonedBytes, _width, _height);
    }
    
    public Task<IImage> ResizeAsync(int width, int height)
    {
        // プレースホルダー実装：リサイズ処理
        var resizedImage = new PlaceholderImageWithSize(_imageBytes, width, height);
        return Task.FromResult<IImage>(resizedImage);
    }
    
    public void Dispose()
    {
        // バイト配列は自動的にガベージコレクションされるため、特別な処理は不要
    }
}