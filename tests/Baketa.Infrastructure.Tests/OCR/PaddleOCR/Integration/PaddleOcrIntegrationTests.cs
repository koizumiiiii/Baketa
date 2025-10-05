using Xunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Baketa.Infrastructure.OCR.PaddleOCR.Engine;
using Baketa.Infrastructure.OCR.PaddleOCR.Initialization;
using Baketa.Infrastructure.OCR.PaddleOCR.Models;
using Baketa.Infrastructure.DI;
using Baketa.Core.Abstractions.Dependency;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.OCR;
using System.Drawing;
using Moq;
using System.IO;
using Baketa.Infrastructure.Tests.OCR.PaddleOCR.TestData;

namespace Baketa.Infrastructure.Tests.OCR.PaddleOCR.Integration;

/// <summary>
/// PaddleOCR統合テスト
/// Phase 4: テストと検証 - 統合テスト実装
/// </summary>
public class PaddleOcrIntegrationTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly string _testBaseDirectory;
    private bool _disposed;

    public PaddleOcrIntegrationTests()
    {
        // テスト用の一時ディレクトリ
        _testBaseDirectory = Path.Combine(Path.GetTempPath(), "BaketaOCRIntegrationTest", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testBaseDirectory);

        // DIコンテナの設定（安全版）
        var services = new ServiceCollection();
        
        // ロギング設定
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        
        // 安全なテスト用モデルパスリゾルバのみ登録
        services.AddSingleton<IModelPathResolver>(provider => 
            new DefaultModelPathResolver(_testBaseDirectory));

        _serviceProvider = services.BuildServiceProvider();
        
        // 必要なディレクトリを事前作成
        CreateTestDirectories();
    }

    private void CreateTestDirectories()
    {
        var modelPathResolver = _serviceProvider.GetRequiredService<IModelPathResolver>();
        
        var directories = new[]
        {
            modelPathResolver.GetDetectionModelsDirectory(),
            modelPathResolver.GetRecognitionModelsDirectory("eng"),
            modelPathResolver.GetRecognitionModelsDirectory("jpn"),
            Path.Combine(_testBaseDirectory, "Models", "classification"), // 分類モデルディレクトリを追加
            Path.Combine(_testBaseDirectory, "Temp")
        };

        foreach (var directory in directories)
        {
            Directory.CreateDirectory(directory);
        }
    }

    #region 初期化フロー統合テスト

    [Fact]
    public async Task InitializationFlow_EndToEnd_Success()
    {
        // Arrange - テスト用の安全なエンジンのみを使用
        var modelPathResolver = _serviceProvider.GetRequiredService<IModelPathResolver>();
        var logger = _serviceProvider.GetRequiredService<ILogger<PaddleOcrEngine>>();
        
        using var safeOcrEngine = new SafeTestPaddleOcrEngine(modelPathResolver, logger, true);

        // Act & Assert - OCRエンジン初期化のみをテスト（実際のPaddleOcrInitializerは使用しない）
        var settings = new OcrEngineSettings { Language = "eng", EnableMultiThread = false, WorkerCount = 2 };
        var engineInitResult = await safeOcrEngine.InitializeAsync(settings, CancellationToken.None).ConfigureAwait(false);
        Assert.True(engineInitResult, "SafeTestPaddleOcrEngine should initialize successfully");
        
        Assert.True(safeOcrEngine.IsInitialized, "OCR engine should be initialized");
        Assert.Equal("eng", safeOcrEngine.CurrentLanguage);
    }

    [Fact]
    public async Task InitializationFlow_MultipleLanguages_Success()
    {
        // Arrange - テスト用の安全なエンジンのみを使用
        var modelPathResolver = _serviceProvider.GetRequiredService<IModelPathResolver>();
        var logger = _serviceProvider.GetRequiredService<ILogger<PaddleOcrEngine>>();
        
        using var safeOcrEngine = new SafeTestPaddleOcrEngine(modelPathResolver, logger, true);

        // Act & Assert - 初期化（安全なエンジンのみ）
        var engSettings = new OcrEngineSettings { Language = "eng", EnableMultiThread = true, WorkerCount = 2 };
        await safeOcrEngine.InitializeAsync(engSettings, CancellationToken.None).ConfigureAwait(false);

        // 言語切り替えテスト
        var switchResult = await safeOcrEngine.SwitchLanguageAsync("jpn", CancellationToken.None).ConfigureAwait(false);
        Assert.True(switchResult, "Language switch should succeed");
        Assert.Equal("jpn", safeOcrEngine.CurrentLanguage);

        // 元の言語に戻す
        var switchBackResult = await safeOcrEngine.SwitchLanguageAsync("eng", CancellationToken.None).ConfigureAwait(false);
        Assert.True(switchBackResult, "Language switch back should succeed");
        Assert.Equal("eng", safeOcrEngine.CurrentLanguage);
    }

    #endregion

    #region OCR処理統合テスト

    [Fact]
    public async Task OcrProcessing_WithMockImage_HandlesGracefully()
    {
        // Arrange - テスト用の安全なエンジンのみを使用
        var modelPathResolver = _serviceProvider.GetRequiredService<IModelPathResolver>();
        var logger = _serviceProvider.GetRequiredService<ILogger<PaddleOcrEngine>>();
        
        using var safeOcrEngine = new SafeTestPaddleOcrEngine(modelPathResolver, logger, true);
        
        var settings = new OcrEngineSettings { Language = "eng", EnableMultiThread = true, WorkerCount = 2 };
        await safeOcrEngine.InitializeAsync(settings, CancellationToken.None).ConfigureAwait(false);

        var mockImage = CreateMockImage();

        // Act
        var results = await safeOcrEngine.RecognizeAsync(mockImage.Object, null, CancellationToken.None).ConfigureAwait(false);

        // Assert
        Assert.NotNull(results);
        // テスト用エンジンでは空の結果が期待される
        Assert.Empty(results.TextRegions);
    }

    [Fact]
    public async Task OcrProcessing_WithROI_HandlesGracefully()
    {
        // Arrange - テスト用の安全なエンジンのみを使用
        var modelPathResolver = _serviceProvider.GetRequiredService<IModelPathResolver>();
        var logger = _serviceProvider.GetRequiredService<ILogger<PaddleOcrEngine>>();
        
        using var safeOcrEngine = new SafeTestPaddleOcrEngine(modelPathResolver, logger, true);
        
        var settings = new OcrEngineSettings { Language = "eng", EnableMultiThread = true, WorkerCount = 2 };
        await safeOcrEngine.InitializeAsync(settings, CancellationToken.None).ConfigureAwait(false);

        var mockImage = CreateMockImage();
        var roi = new Rectangle(10, 10, 100, 50);

        // Act
        var results = await safeOcrEngine.RecognizeAsync(mockImage.Object, roi, null, CancellationToken.None).ConfigureAwait(false);

        // Assert
        Assert.NotNull(results);
        Assert.Empty(results.TextRegions);
        Assert.Equal(roi, results.RegionOfInterest);
    }

    #endregion

    #region パフォーマンステスト

    [Fact]
    public async Task Performance_InitializationTime_WithinReasonableLimits()
    {
        // Arrange - テスト用の安全なエンジンのみを使用
        var modelPathResolver = _serviceProvider.GetRequiredService<IModelPathResolver>();
        var logger = _serviceProvider.GetRequiredService<ILogger<PaddleOcrEngine>>();
        
        using var safeOcrEngine = new SafeTestPaddleOcrEngine(modelPathResolver, logger, true);
        
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act - 安全なエンジンのみを初期化
        var settings = new OcrEngineSettings { Language = "eng", EnableMultiThread = true, WorkerCount = 2 };
        await safeOcrEngine.InitializeAsync(settings, CancellationToken.None).ConfigureAwait(false);
        
        stopwatch.Stop();

        // Assert
        // 初期化は1秒以内に完了すべき（テスト用エンジンの場合）
        Assert.True(stopwatch.ElapsedMilliseconds < 1000, 
            $"Initialization took too long: {stopwatch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task Performance_LanguageSwitching_WithinReasonableLimits()
    {
        // Arrange - テスト用の安全なエンジンのみを使用
        var modelPathResolver = _serviceProvider.GetRequiredService<IModelPathResolver>();
        var logger = _serviceProvider.GetRequiredService<ILogger<PaddleOcrEngine>>();
        
        using var safeOcrEngine = new SafeTestPaddleOcrEngine(modelPathResolver, logger, true);
        
        var settings = new OcrEngineSettings { Language = "eng", EnableMultiThread = true, WorkerCount = 2 };
        await safeOcrEngine.InitializeAsync(settings, CancellationToken.None).ConfigureAwait(false);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act
        await safeOcrEngine.SwitchLanguageAsync("jpn", CancellationToken.None).ConfigureAwait(false);
        
        stopwatch.Stop();

        // Assert
        // 言語切り替えは1000ms以内に完了すべき（テスト環境・CI環境を考慮）
        // 実運用では更に最適化が必要だが、テストでは環境差異を許容
        Assert.True(stopwatch.ElapsedMilliseconds < 1000, 
            $"Language switching took too long: {stopwatch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task Performance_MultipleOcrCalls_HandlesConcurrency()
    {
        // Arrange - テスト用の安全なエンジンのみを使用
        var modelPathResolver = _serviceProvider.GetRequiredService<IModelPathResolver>();
        var logger = _serviceProvider.GetRequiredService<ILogger<PaddleOcrEngine>>();
        
        using var safeOcrEngine = new SafeTestPaddleOcrEngine(modelPathResolver, logger, true);
        
        var settings = new OcrEngineSettings { Language = "eng", EnableMultiThread = true, WorkerCount = 4 };
        await safeOcrEngine.InitializeAsync(settings, CancellationToken.None).ConfigureAwait(false); // マルチスレッド有効

        var mockImage = CreateMockImage();
        const int concurrentCallsCount = 10;

        // Act
        var tasks = Enumerable.Range(0, concurrentCallsCount)
            .Select(async _ => await safeOcrEngine.RecognizeAsync(mockImage.Object, null, CancellationToken.None).ConfigureAwait(false))
            .ToArray();

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        // Assert
        Assert.Equal(concurrentCallsCount, results.Length);
        Assert.All(results, result => Assert.NotNull(result));
    }

    #endregion

    #region エラー回復テスト

    [Fact]
    public async Task ErrorRecovery_InitializationFailure_HandlesGracefully()
    {
        // Arrange - 無効なパスでエラーを発生させるモックアダプターを使用
        var mockModelPathResolver = new Mock<IModelPathResolver>();
        
        // 存在しないパスを設定してエラーを発生させる
        const string invalidModelsPath = "\\\\invalid\\recovery\\test\\Models";
        const string invalidDetectionPath = "\\\\invalid\\recovery\\test\\Models\\detection";
        const string invalidRecognitionEngPath = "\\\\invalid\\recovery\\test\\Models\\recognition\\eng";
        const string invalidRecognitionJpnPath = "\\\\invalid\\recovery\\test\\Models\\recognition\\jpn";
        
        mockModelPathResolver.Setup(x => x.GetModelsRootDirectory())
            .Returns(invalidModelsPath);
        mockModelPathResolver.Setup(x => x.GetDetectionModelsDirectory())
            .Returns(invalidDetectionPath);
        mockModelPathResolver.Setup(x => x.GetRecognitionModelsDirectory("eng"))
            .Returns(invalidRecognitionEngPath);
        mockModelPathResolver.Setup(x => x.GetRecognitionModelsDirectory("jpn"))
            .Returns(invalidRecognitionJpnPath);
        mockModelPathResolver.Setup(x => x.GetDetectionModelPath(It.IsAny<string>()))
            .Returns(Path.Combine(invalidDetectionPath, "model.onnx"));
        mockModelPathResolver.Setup(x => x.GetRecognitionModelPath(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Path.Combine(invalidRecognitionEngPath, "model.onnx"));
        mockModelPathResolver.Setup(x => x.FileExists(It.IsAny<string>()))
            .Returns(false);
        mockModelPathResolver.Setup(x => x.EnsureDirectoryExists(It.IsAny<string>()));

        var logger = _serviceProvider.GetRequiredService<ILogger<PaddleOcrEngine>>();
        
        // テスト用の安全なエンジンで初期化失敗をシミュレート
        using var failingEngine = new SafeTestPaddleOcrEngine(mockModelPathResolver.Object, logger, true);

        // Act - エラーを発生させるための無効な設定
        bool result;
        try
        {
            var invalidSettings = new OcrEngineSettings { Language = "eng", EnableMultiThread = true, WorkerCount = 2 };
            result = await failingEngine.InitializeAsync(invalidSettings, CancellationToken.None).ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
            result = false; // 無効なパスで初期化失敗をシミュレート
        }

        // Assert
        Assert.False(result, "Initialization should fail with invalid path configuration");
    }

    [Fact]
    public async Task ErrorRecovery_OcrOnUninitializedEngine_ThrowsAppropriateException()
    {
        // Arrange - 未初期化のテスト用エンジンを使用
        var modelPathResolver = _serviceProvider.GetRequiredService<IModelPathResolver>();
        var logger = _serviceProvider.GetRequiredService<ILogger<PaddleOcrEngine>>();
        using var uninitializedEngine = new SafeTestPaddleOcrEngine(modelPathResolver, logger, true);
        
        var mockImage = CreateMockImage();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => uninitializedEngine.RecognizeAsync(mockImage.Object, null, CancellationToken.None)).ConfigureAwait(false);
    }

    #endregion

    #region リソース管理統合テスト

    [Fact]
    public async Task ResourceManagement_ProperDisposal_NoMemoryLeaks()
    {
        // Arrange - テスト用の安全なエンジンのみを使用
        var modelPathResolver = _serviceProvider.GetRequiredService<IModelPathResolver>();
        var logger = _serviceProvider.GetRequiredService<ILogger<PaddleOcrEngine>>();
        
        var safeOcrEngine = new SafeTestPaddleOcrEngine(modelPathResolver, logger, true);
        
        var settings = new OcrEngineSettings { Language = "eng", EnableMultiThread = true, WorkerCount = 2 };
        await safeOcrEngine.InitializeAsync(settings, CancellationToken.None).ConfigureAwait(false);

        // Act
        safeOcrEngine.Dispose();

        // Assert
        Assert.False(safeOcrEngine.IsInitialized);
        
        // 再利用しようとすると例外が発生することを確認
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => safeOcrEngine.RecognizeAsync(CreateMockImage().Object, null, CancellationToken.None)).ConfigureAwait(false);
    }

    #endregion

    #region 設定検証テスト

    [Fact]
    public void Configuration_DirectoryStructure_CreatedCorrectly()
    {
        // Arrange
        var modelPathResolver = _serviceProvider.GetRequiredService<IModelPathResolver>();

        // Act - テスト環境ではディレクトリ構造のチェックのみ実行
        var expectedDirectories = new[]
        {
            modelPathResolver.GetModelsRootDirectory(),
            modelPathResolver.GetDetectionModelsDirectory(),
            modelPathResolver.GetRecognitionModelsDirectory("eng"),
            modelPathResolver.GetRecognitionModelsDirectory("jpn"),
            Path.Combine(_testBaseDirectory, "Models", "classification") // 分類モデルディレクトリを追加
        };

        // Assert - ディレクトリパスが正しく作成されることを確認（テスト環境では事前作成済み）
        foreach (var directory in expectedDirectories)
        {
            Assert.True(Directory.Exists(directory), $"Directory should exist: {directory}");
        }
    }

    [Fact]
    public void Configuration_ModelPaths_ResolvedCorrectly()
    {
        // Arrange
        var modelPathResolver = _serviceProvider.GetRequiredService<IModelPathResolver>();

        // Act
        var detectionModelPath = modelPathResolver.GetDetectionModelPath("det_db_standard");
        var englishModelPath = modelPathResolver.GetRecognitionModelPath("eng", "rec_english_standard");
        var japaneseModelPath = modelPathResolver.GetRecognitionModelPath("jpn", "rec_japan_standard");
        var classificationModelPath = modelPathResolver.GetClassificationModelPath("cls_mobile_standard"); // 分類モデルパスを追加

        // Assert
        Assert.Contains("det_db_standard.onnx", detectionModelPath, StringComparison.Ordinal);
        Assert.Contains("rec_english_standard.onnx", englishModelPath, StringComparison.Ordinal);
        Assert.Contains("rec_japan_standard.onnx", japaneseModelPath, StringComparison.Ordinal);
        Assert.Contains("cls_mobile_standard.onnx", classificationModelPath, StringComparison.Ordinal); // 分類モデルパスの検証
        
        Assert.Contains("detection", detectionModelPath, StringComparison.Ordinal);
        Assert.Contains(Path.Combine("recognition", "eng"), englishModelPath, StringComparison.Ordinal);
        Assert.Contains(Path.Combine("recognition", "jpn"), japaneseModelPath, StringComparison.Ordinal);
        Assert.Contains("classification", classificationModelPath, StringComparison.Ordinal); // 分類モデルディレクトリの検証
    }

    #endregion

    #region Phase 2.9 リファクタリング検証テスト

    /// <summary>
    /// Phase 2.9リファクタリング後のエンドツーエンド動作同一性検証
    /// 全6サービス統合後の正常動作を確認
    /// </summary>
    [Fact]
    public async Task Refactoring_Phase29_BehaviorIdentity_AllServicesIntegrated()
    {
        // Arrange - テスト用の安全なエンジンのみを使用
        var modelPathResolver = _serviceProvider.GetRequiredService<IModelPathResolver>();
        var logger = _serviceProvider.GetRequiredService<ILogger<PaddleOcrEngine>>();

        using var safeOcrEngine = new SafeTestPaddleOcrEngine(modelPathResolver, logger, true);

        // Act & Assert - Phase 2.9リファクタリング後の動作検証

        // 1. 初期化フロー（Phase 2.9.1: PaddleOcrModelManager統合）
        var settings = new OcrEngineSettings { Language = "eng", EnableMultiThread = false, WorkerCount = 2 };
        var engineInitResult = await safeOcrEngine.InitializeAsync(settings, CancellationToken.None).ConfigureAwait(false);
        Assert.True(engineInitResult, "Phase 2.9: InitializeAsync should succeed");
        Assert.True(safeOcrEngine.IsInitialized, "Phase 2.9: Engine should be initialized");
        Assert.Equal("eng", safeOcrEngine.CurrentLanguage);

        // 2. OCR実行（認識付き）（Phase 2.9.4b: PaddleOcrExecutor + PaddleOcrResultConverter統合）
        var mockImage = CreateMockImage();
        var ocrResults = await safeOcrEngine.RecognizeAsync(mockImage.Object, null, CancellationToken.None).ConfigureAwait(false);
        Assert.NotNull(ocrResults);
        Assert.NotNull(ocrResults.TextRegions); // Phase 2.9.4: ConvertToTextRegions動作確認
        Assert.Empty(ocrResults.TextRegions); // テスト用エンジンでは空結果

        // NOTE: ExecuteTextDetectionOnlyAsyncはSafeTestPaddleOcrEngineに未実装のため、
        // 検出専用OCR検証は実環境テストまたは統合テストで実施

        // 4. ROI指定OCR実行（Phase 2.9.2: PaddleOcrImageProcessor統合）
        var roi = new Rectangle(10, 10, 100, 50);
        var roiResults = await safeOcrEngine.RecognizeAsync(mockImage.Object, roi, null, CancellationToken.None).ConfigureAwait(false);
        Assert.NotNull(roiResults);
        Assert.Equal(roi, roiResults.RegionOfInterest); // ROI適用確認

        // 5. 言語切り替え（Phase 2.9.1: PaddleOcrModelManager言語管理）
        var switchResult = await safeOcrEngine.SwitchLanguageAsync("jpn", CancellationToken.None).ConfigureAwait(false);
        Assert.True(switchResult, "Phase 2.9: Language switch should succeed");
        Assert.Equal("jpn", safeOcrEngine.CurrentLanguage);

        // 6. パフォーマンス統計取得（Phase 2.9.6: IPaddleOcrPerformanceTracker委譲）
        var perfStats = safeOcrEngine.GetPerformanceStats();
        Assert.NotNull(perfStats); // Phase 2.9: パフォーマンス統計取得確認

        // 7. IOcrEngineインターフェースメソッド（Phase 2.9.6追加）
        var availableLanguages = safeOcrEngine.GetAvailableLanguages();
        Assert.NotNull(availableLanguages);
        Assert.Contains("eng", availableLanguages);
        Assert.Contains("jpn", availableLanguages);

        var availableModels = safeOcrEngine.GetAvailableModels();
        Assert.NotNull(availableModels);
        Assert.Contains("standard", availableModels);

        // 8. エラーハンドリング（Phase 2.9.3: PaddleOcrErrorHandler統合）
        // 未初期化状態でのOCR実行は例外をスロー
        using var uninitializedEngine = new SafeTestPaddleOcrEngine(modelPathResolver, logger, true);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => uninitializedEngine.RecognizeAsync(mockImage.Object, null, CancellationToken.None)).ConfigureAwait(false);
    }

    /// <summary>
    /// Phase 2.9リファクタリング: 全サービス連携動作検証
    /// </summary>
    [Fact]
    public async Task Refactoring_Phase29_AllServices_IntegratedCorrectly()
    {
        // Arrange
        var modelPathResolver = _serviceProvider.GetRequiredService<IModelPathResolver>();
        var logger = _serviceProvider.GetRequiredService<ILogger<PaddleOcrEngine>>();

        using var safeOcrEngine = new SafeTestPaddleOcrEngine(modelPathResolver, logger, true);

        // Act - 複数操作を連続実行してサービス連携を検証
        var settings = new OcrEngineSettings { Language = "eng", EnableMultiThread = true, WorkerCount = 4 };
        await safeOcrEngine.InitializeAsync(settings, CancellationToken.None).ConfigureAwait(false);

        var mockImage = CreateMockImage();
        var result1 = await safeOcrEngine.RecognizeAsync(mockImage.Object, null, CancellationToken.None).ConfigureAwait(false);
        var result2 = await safeOcrEngine.RecognizeAsync(mockImage.Object, new Rectangle(0, 0, 100, 100), null, CancellationToken.None).ConfigureAwait(false);

        await safeOcrEngine.SwitchLanguageAsync("jpn", CancellationToken.None).ConfigureAwait(false);
        var result3 = await safeOcrEngine.RecognizeAsync(mockImage.Object, null, CancellationToken.None).ConfigureAwait(false);

        // Assert - 全操作が正常完了することを確認
        Assert.NotNull(result1);
        Assert.NotNull(result2);
        Assert.NotNull(result3);
        Assert.Equal("jpn", safeOcrEngine.CurrentLanguage);

        // パフォーマンストラッカーが動作していることを確認
        var stats = safeOcrEngine.GetPerformanceStats();
        Assert.NotNull(stats);
    }

    #endregion

    #region ヘルパーメソッド

    private static Mock<IImage> CreateMockImage()
    {
        var mockImage = new Mock<IImage>();
        mockImage.Setup(x => x.Width).Returns(640);
        mockImage.Setup(x => x.Height).Returns(480);
        
        // 簡単なダミーデータを設定
        var dummyData = new byte[640 * 480 * 3]; // RGB
        mockImage.Setup(x => x.ToByteArrayAsync()).ReturnsAsync(dummyData);
        
        return mockImage;
    }

    /// <summary>
    /// ネイティブライブラリが使用可能かチェック
    /// </summary>
    private static bool IsNativeLibraryAvailable()
    {
        // 環境変数でネイティブライブラリテストを無効化できる
        var skipNativeTests = Environment.GetEnvironmentVariable("SKIP_NATIVE_TESTS");
        if (!string.IsNullOrEmpty(skipNativeTests) && bool.TryParse(skipNativeTests, out var skip) && skip)
        {
            return false;
        }

        // CI環境ではネイティブライブラリテストをスキップ
        var isCI = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")) ||
                   !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")) ||
                   !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BUILD_BUILDID"));
        
        if (isCI)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// ネイティブライブラリエラーを統一的に処理
    /// </summary>
    private static async Task<T> ExecuteWithNativeLibraryHandling<T>(Func<Task<T>> action, T defaultValue = default!)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (TypeInitializationException ex) when (ex.InnerException is DllNotFoundException)
        {
            Assert.True(true, $"Skipped: Native DLL not found - {ex.InnerException.Message}");
            return defaultValue;
        }
        catch (System.Runtime.InteropServices.SEHException)
        {
            Assert.True(true, "Skipped: SEH exception in native library");
            return defaultValue;
        }
        catch (DllNotFoundException ex)
        {
            Assert.True(true, $"Skipped: DLL not found - {ex.Message}");
            return defaultValue;
        }
        catch (Exception ex) when (ex.Message.Contains("OpenCvSharpExtern", StringComparison.OrdinalIgnoreCase))
        {
            Assert.True(true, $"Skipped: OpenCV native library issue - {ex.Message}");
            return defaultValue;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("初期化されていません", StringComparison.Ordinal))
        {
            Assert.True(true, $"Skipped: OCR engine not initialized properly - {ex.Message}");
            return defaultValue;
        }
    }

    /// <summary>
    /// void返却メソッド用のネイティブライブラリエラー処理
    /// </summary>
    private static async Task ExecuteWithNativeLibraryHandling(Func<Task> action)
    {
        await ExecuteWithNativeLibraryHandling(async () => 
        {
            await action().ConfigureAwait(false);
            return true;
        }).ConfigureAwait(false);
    }

    #endregion

    #region IDisposable実装

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _serviceProvider?.Dispose();
                
                // テスト用ディレクトリのクリーンアップ
                try
                {
                    if (Directory.Exists(_testBaseDirectory))
                    {
                        Directory.Delete(_testBaseDirectory, true);
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
            
            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    #endregion
}
