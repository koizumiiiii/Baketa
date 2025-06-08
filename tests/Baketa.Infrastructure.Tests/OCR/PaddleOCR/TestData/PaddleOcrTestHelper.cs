using System.Drawing;
using System.IO;
using Moq;
using Xunit;
using Baketa.Core.Abstractions.Imaging;

namespace Baketa.Infrastructure.Tests.OCR.PaddleOCR.TestData;

/// <summary>
/// PaddleOCRテスト用のヘルパークラス
/// Phase 4: テストと検証 - テストサポート機能
/// </summary>
public static class PaddleOcrTestHelper
{
    #region テスト画像作成

    /// <summary>
    /// モックImageインスタンスを作成
    /// </summary>
    /// <param name="width">画像幅</param>
    /// <param name="height">画像高さ</param>
    /// <param name="channels">チャンネル数（RGB=3, RGBA=4）</param>
    /// <returns>モックImageインスタンス</returns>
    public static Mock<IImage> CreateMockImage(int width = 640, int height = 480, int channels = 3)
    {
        var mockImage = new Mock<IImage>();
        mockImage.Setup(x => x.Width).Returns(width);
        mockImage.Setup(x => x.Height).Returns(height);
        
        // ダミーの画像データを生成
        var imageData = CreateDummyImageData(width, height, channels);
        mockImage.Setup(x => x.ToByteArrayAsync()).ReturnsAsync(imageData);
        
        return mockImage;
    }

    /// <summary>
    /// テスト用のダミー画像データを生成
    /// </summary>
    /// <param name="width">画像幅</param>
    /// <param name="height">画像高さ</param>
    /// <param name="channels">チャンネル数</param>
    /// <returns>ダミー画像データ</returns>
    public static byte[] CreateDummyImageData(int width, int height, int channels = 3)
    {
        var dataSize = width * height * channels;
        var imageData = new byte[dataSize];
        
        // 単純なグラデーションパターンを作成
        for (int i = 0; i < dataSize; i++)
        {
            imageData[i] = (byte)(i % 256);
        }
        
        return imageData;
    }

    /// <summary>
    /// テスト用の英語テキスト画像を模擬
    /// </summary>
    /// <returns>英語テキストを含む模擬画像</returns>
    public static Mock<IImage> CreateEnglishTextMockImage()
    {
        return CreateMockImage(800, 100, 3); // 横長の英語テキスト想定
    }

    /// <summary>
    /// テスト用の日本語テキスト画像を模擬
    /// </summary>
    /// <returns>日本語テキストを含む模擬画像</returns>
    public static Mock<IImage> CreateJapaneseTextMockImage()
    {
        return CreateMockImage(400, 200, 3); // 縦横比の異なる日本語テキスト想定
    }

    /// <summary>
    /// 小さなROI領域用の画像を模擬
    /// </summary>
    /// <returns>小領域テキストを含む模擬画像</returns>
    public static Mock<IImage> CreateSmallROIMockImage()
    {
        return CreateMockImage(100, 50, 3); // 小さなROI想定
    }

    #endregion

    #region テストROI作成

    /// <summary>
    /// 標準的なROI領域を作成
    /// </summary>
    /// <param name="imageWidth">画像幅</param>
    /// <param name="imageHeight">画像高さ</param>
    /// <returns>中央部分のROI</returns>
    public static Rectangle CreateCenterROI(int imageWidth, int imageHeight)
    {
        var roiWidth = imageWidth / 2;
        var roiHeight = imageHeight / 2;
        var x = (imageWidth - roiWidth) / 2;
        var y = (imageHeight - roiHeight) / 2;
        
        return new Rectangle(x, y, roiWidth, roiHeight);
    }

    /// <summary>
    /// 上部領域のROIを作成（UIテキスト想定）
    /// </summary>
    /// <param name="imageWidth">画像幅</param>
    /// <param name="imageHeight">画像高さ</param>
    /// <returns>上部のROI</returns>
    public static Rectangle CreateTopROI(int imageWidth, int imageHeight)
    {
        var roiHeight = imageHeight / 4; // 上部1/4
        return new Rectangle(0, 0, imageWidth, roiHeight);
    }

    /// <summary>
    /// 下部領域のROIを作成（字幕想定）
    /// </summary>
    /// <param name="imageWidth">画像幅</param>
    /// <param name="imageHeight">画像高さ</param>
    /// <returns>下部のROI</returns>
    public static Rectangle CreateBottomROI(int imageWidth, int imageHeight)
    {
        var roiHeight = imageHeight / 6; // 下部1/6
        var y = imageHeight - roiHeight;
        return new Rectangle(0, y, imageWidth, roiHeight);
    }

    /// <summary>
    /// 複数のROI領域を作成
    /// </summary>
    /// <param name="imageWidth">画像幅</param>
    /// <param name="imageHeight">画像高さ</param>
    /// <returns>複数ROI配列</returns>
    public static Rectangle[] CreateMultipleROIs(int imageWidth, int imageHeight)
    {
        return
        [
        CreateTopROI(imageWidth, imageHeight),
        CreateCenterROI(imageWidth, imageHeight),
        CreateBottomROI(imageWidth, imageHeight)
        ];
    }

    #endregion

    #region テストデータ生成

    /// <summary>
    /// 有効な言語コードリストを取得
    /// </summary>
    /// <returns>テスト用言語コード配列</returns>
    public static string[] GetValidLanguageCodes()
    {
        return ["eng", "jpn", "chs", "cht", "kor", "fra", "spa", "deu", "rus"];
    }

    /// <summary>
    /// 無効な言語コードリストを取得
    /// </summary>
    /// <returns>無効な言語コード配列</returns>
    public static string[] GetInvalidLanguageCodes()
    {
        return ["", "invalid", "xxx", "12345", "あいう"];
    }

    /// <summary>
    /// 有効なモデル名リストを取得
    /// </summary>
    /// <returns>テスト用モデル名配列</returns>
    public static string[] GetValidModelNames()
    {
        return
        [
            "det_db_standard",
            "det_db_lite",
            "rec_english_standard",
            "rec_japan_standard",
            "cls_standard",
            "custom_model_v1"
        ];
    }

    /// <summary>
    /// 無効なモデル名リストを取得
    /// </summary>
    /// <returns>無効なモデル名配列</returns>
    public static string[] GetInvalidModelNames()
    {
        return ["", "   ", null!, "model with spaces", "model/with/slashes"];
    }

    #endregion

    #region テスト用ディレクトリ管理

    /// <summary>
    /// テスト用の一時ディレクトリを作成
    /// </summary>
    /// <param name="testName">テスト名（ディレクトリ名に使用）</param>
    /// <returns>作成されたディレクトリパス</returns>
    public static string CreateTestDirectory(string testName)
    {
        var testDirectory = Path.Combine(
            Path.GetTempPath(), 
            "BaketaOCRTests", 
            testName, 
            Guid.NewGuid().ToString());
        
        Directory.CreateDirectory(testDirectory);
        return testDirectory;
    }

    /// <summary>
    /// テスト用ディレクトリを削除
    /// </summary>
    /// <param name="directoryPath">削除するディレクトリパス</param>
    public static void CleanupTestDirectory(string directoryPath)
    {
        try
        {
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, true);
            }
        }
        catch (UnauthorizedAccessException)
        {
            // アクセス権限エラーは無視
        }
        catch (DirectoryNotFoundException)
        {
            // ディレクトリが既に削除されている場合は無視
        }
        catch (IOException)
        {
            // I/Oエラーは無視
        }
    }

    /// <summary>
    /// テスト用モデルディレクトリ構造を作成
    /// </summary>
    /// <param name="baseDirectory">ベースディレクトリ</param>
    public static void CreateModelDirectoryStructure(string baseDirectory)
    {
        var directories = new[]
        {
            Path.Combine(baseDirectory, "Models"),
            Path.Combine(baseDirectory, "Models", "detection"),
            Path.Combine(baseDirectory, "Models", "recognition", "eng"),
            Path.Combine(baseDirectory, "Models", "recognition", "jpn"),
            Path.Combine(baseDirectory, "Models", "recognition", "chs"),
            Path.Combine(baseDirectory, "Models", "classification"),
            Path.Combine(baseDirectory, "Temp")
        };

        foreach (var directory in directories)
        {
            Directory.CreateDirectory(directory);
        }
    }

    #endregion

    #region パフォーマンステスト用ヘルパー

    /// <summary>
    /// 実行時間を測定してアクションを実行
    /// </summary>
    /// <param name="action">測定対象のアクション</param>
    /// <returns>実行時間（ミリ秒）</returns>
    public static long MeasureExecutionTime(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        action();
        stopwatch.Stop();
        return stopwatch.ElapsedMilliseconds;
    }

    /// <summary>
    /// 非同期実行時間を測定してタスクを実行
    /// </summary>
    /// <param name="taskFunc">測定対象のタスク関数</param>
    /// <returns>実行時間（ミリ秒）</returns>
    public static async Task<long> MeasureExecutionTimeAsync(Func<Task> taskFunc)
    {
        ArgumentNullException.ThrowIfNull(taskFunc);
        
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await taskFunc().ConfigureAwait(false);
        stopwatch.Stop();
        return stopwatch.ElapsedMilliseconds;
    }

    /// <summary>
    /// 指定回数実行して平均実行時間を測定
    /// </summary>
    /// <param name="action">測定対象のアクション</param>
    /// <param name="iterations">実行回数</param>
    /// <returns>平均実行時間（ミリ秒）</returns>
    public static double MeasureAverageExecutionTime(Action action, int iterations = 10)
    {
        var times = new List<long>();
        
        for (int i = 0; i < iterations; i++)
        {
            times.Add(MeasureExecutionTime(action));
        }
        
        return times.Average();
    }

    #endregion

    #region アサーションヘルパー

    /// <summary>
    /// パスが有効なファイルパスであることを検証
    /// </summary>
    /// <param name="path">検証対象のパス</param>
    /// <param name="expectedExtension">期待される拡張子</param>
    public static void AssertValidFilePath(string path, string expectedExtension = ".onnx")
    {
        ArgumentNullException.ThrowIfNull(path);
        
        Assert.NotNull(path);
        Assert.NotEmpty(path);
        Assert.True(Path.IsPathRooted(path), $"Path should be rooted: {path}");
        Assert.EndsWith(expectedExtension, path, StringComparison.OrdinalIgnoreCase);
        
        // 無効な文字が含まれていないことを確認
        var invalidChars = Path.GetInvalidPathChars();
        Assert.False(path.Any(c => invalidChars.Contains(c)), $"Path contains invalid characters: {path}");
    }

    /// <summary>
    /// ディレクトリパスが有効であることを検証
    /// </summary>
    /// <param name="directoryPath">検証対象のディレクトリパス</param>
    public static void AssertValidDirectoryPath(string directoryPath)
    {
        ArgumentNullException.ThrowIfNull(directoryPath);
        
        Assert.NotNull(directoryPath);
        Assert.NotEmpty(directoryPath);
        Assert.True(Path.IsPathRooted(directoryPath), $"Directory path should be rooted: {directoryPath}");
        
        // 無効な文字が含まれていないことを確認
        var invalidChars = Path.GetInvalidPathChars();
        Assert.False(directoryPath.Any(c => invalidChars.Contains(c)), 
            $"Directory path contains invalid characters: {directoryPath}");
    }

    /// <summary>
    /// ROIが画像サイズ内に収まっていることを検証
    /// </summary>
    /// <param name="roi">検証対象のROI</param>
    /// <param name="imageWidth">画像幅</param>
    /// <param name="imageHeight">画像高さ</param>
    public static void AssertValidROI(Rectangle roi, int imageWidth, int imageHeight)
    {
        Assert.True(roi.X >= 0, "ROI X should be non-negative");
        Assert.True(roi.Y >= 0, "ROI Y should be non-negative");
        Assert.True(roi.Width > 0, "ROI Width should be positive");
        Assert.True(roi.Height > 0, "ROI Height should be positive");
        Assert.True(roi.Right <= imageWidth, $"ROI should not exceed image width: {roi.Right} <= {imageWidth}");
        Assert.True(roi.Bottom <= imageHeight, $"ROI should not exceed image height: {roi.Bottom} <= {imageHeight}");
    }

    #endregion
}
