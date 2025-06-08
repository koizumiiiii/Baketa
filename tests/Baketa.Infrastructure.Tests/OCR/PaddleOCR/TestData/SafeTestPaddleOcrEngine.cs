using Microsoft.Extensions.Logging;
using Baketa.Infrastructure.OCR.PaddleOCR.Engine;
using Baketa.Infrastructure.OCR.PaddleOCR.Models;
using Baketa.Core.Abstractions.Imaging;
using System.Drawing;

namespace Baketa.Infrastructure.Tests.OCR.PaddleOCR.TestData;

/// <summary>
/// テスト用の安全なPaddleOcrEngineラッパー
/// 実際のPaddleOCRライブラリを使用せずに、引数検証と基本的な動作をテストします
/// </summary>
public class SafeTestPaddleOcrEngine : IDisposable
{
    private readonly IModelPathResolver _modelPathResolver;
    private readonly ILogger<PaddleOcrEngine>? _logger;
    private readonly bool _skipRealInitialization;
    private bool _disposed;
    
    // テスト用の状態管理
    public bool IsInitialized { get; private set; }
    public string? CurrentLanguage { get; private set; }
    public bool IsMultiThreadEnabled { get; private set; }

    public SafeTestPaddleOcrEngine(
        IModelPathResolver modelPathResolver,
        ILogger<PaddleOcrEngine>? logger = null,
        bool skipRealInitialization = true)
    {
        _modelPathResolver = modelPathResolver ?? throw new ArgumentNullException(nameof(modelPathResolver));
        _logger = logger;
        _skipRealInitialization = skipRealInitialization;
    }

    /// <summary>
    /// OCRエンジンを初期化（テスト用）
    /// </summary>
    public async Task<bool> InitializeAsync(
        string language = "eng",
        bool useGpu = false,
        bool enableMultiThread = false,
        int consumerCount = 2)
    {
        if (_skipRealInitialization)
        {
            // テスト用のモック初期化
            return await SimulateInitializationAsync(language, useGpu, enableMultiThread, consumerCount).ConfigureAwait(false);
        }

        // 実際のPaddleOcrEngineは使用しない（テスト環境では危険）
        throw new NotSupportedException("実際のPaddleOCRエンジンの初期化はテスト環境では無効化されています");
    }

    /// <summary>
    /// 言語の切り替え（テスト用）
    /// </summary>
    public async Task<bool> SwitchLanguageAsync(
        string language,
        bool useGpu = false,
        bool enableMultiThread = false,
        int consumerCount = 2)
    {
        if (_skipRealInitialization)
        {
            return await SimulateSwitchLanguageAsync(language, useGpu, enableMultiThread, consumerCount).ConfigureAwait(false);
        }

        throw new NotSupportedException("実際のPaddleOCRエンジンの言語切り替えはテスト環境では無効化されています");
    }

    /// <summary>
    /// OCR実行（テスト用）
    /// </summary>
    public async Task<object[]> RecognizeAsync(
    IImage image, 
    CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return await RecognizeAsync(image, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// ROI指定でOCR実行（テスト用）
    /// </summary>
    public async Task<object[]> RecognizeAsync(
    IImage image,
    Rectangle? regionOfInterest = null,
    CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ThrowIfDisposed();
        ThrowIfNotInitialized();

        // テスト用のダミー結果を返す
        await Task.Delay(1, cancellationToken).ConfigureAwait(false); // テスト用の最短遅延
        
        _logger?.LogDebug("テスト用OCR実行 - ダミー結果を返却");
        return [];
    }

    /// <summary>
    /// テスト用の初期化シミュレーション
    /// </summary>
    private async Task<bool> SimulateInitializationAsync(string language, bool useGpu, bool enableMultiThread, int consumerCount)
    {
        ThrowIfDisposed();
        
        await Task.Delay(1).ConfigureAwait(false); // 非同期メソッドのダミー待機

        // 引数検証のみ実行（実際のPaddleOcrEngineと同じ検証）
        if (string.IsNullOrWhiteSpace(language))
            throw new ArgumentException("言語コードが無効です", nameof(language));
        
        if (!IsValidLanguage(language))
            throw new ArgumentException($"サポートされていない言語: {language}", nameof(language));
        
        if (consumerCount < 1 || consumerCount > 10)
            throw new ArgumentOutOfRangeException(nameof(consumerCount), "コンシューマー数は1-10の範囲で指定してください");

        // 無効なパス設定を検出
        if (IsInvalidPathConfiguration())
        {
            _logger?.LogError("無効なパス設定で初期化が失敗しました（テスト用）");
            return false;
        }

        if (IsInitialized)
        {
            _logger?.LogDebug("PaddleOCRエンジンは既に初期化されています（テスト用）");
            return true;
        }

        try
        {
            // テスト用のディレクトリ作成シミュレーション
            CreateTestDirectories();
            
            // 成功をシミュレート
            IsInitialized = true;
            CurrentLanguage = language;
            IsMultiThreadEnabled = enableMultiThread;
            
            _logger?.LogInformation("PaddleOCRエンジンの初期化完了（テスト用）");
            return true;
        }
        catch (ArgumentException ex)
        {
            _logger?.LogError(ex, "PaddleOCRエンジンの初期化に失敗（テスト用） - 引数エラー");
            return false;
        }
        catch (InvalidOperationException ex)
        {
            _logger?.LogError(ex, "PaddleOCRエンジンの初期化に失敗（テスト用） - 操作エラー");
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger?.LogError(ex, "PaddleOCRエンジンの初期化に失敗（テスト用） - アクセス権限エラー");
            return false;
        }
        catch (System.IO.IOException ex)
        {
            _logger?.LogError(ex, "PaddleOCRエンジンの初期化に失敗（テスト用） - I/Oエラー");
            return false;
        }
    }

    /// <summary>
    /// テスト用の言語切り替えシミュレーション
    /// </summary>
    private async Task<bool> SimulateSwitchLanguageAsync(string language, bool useGpu, bool enableMultiThread, int consumerCount)
    {
        ThrowIfDisposed();
        
        // パフォーマンステスト用の遅延を最短に
        await Task.Delay(1).ConfigureAwait(false); // テスト用の最短待機時間

        // 引数検証
        if (string.IsNullOrWhiteSpace(language))
            throw new ArgumentException("言語コードが無効です", nameof(language));
        
        if (!IsValidLanguage(language))
            throw new ArgumentException($"サポートされていない言語: {language}", nameof(language));
        
        if (consumerCount < 1 || consumerCount > 10)
            throw new ArgumentOutOfRangeException(nameof(consumerCount), "コンシューマー数は1-10の範囲で指定してください");

        if (!IsInitialized)
        {
            throw new InvalidOperationException("OCRエンジンが初期化されていません。InitializeAsync()を先に呼び出してください。");
        }

        if (CurrentLanguage == language)
        {
            _logger?.LogDebug("既に指定された言語で初期化されています: {Language}（テスト用）", language);
            return true;
        }

        // 言語切り替えをシミュレート
        CurrentLanguage = language;
        IsMultiThreadEnabled = enableMultiThread;
        
        _logger?.LogInformation("言語切り替え完了: {Language}（テスト用）", language);
        return true;
    }

    /// <summary>
    /// 無効なパス設定を検出
    /// </summary>
    private bool IsInvalidPathConfiguration()
    {
        try
        {
            var modelsDirectory = _modelPathResolver.GetModelsRootDirectory();
            var detectionDirectory = _modelPathResolver.GetDetectionModelsDirectory();
            
            // ネットワークパスを検出
            if (modelsDirectory.StartsWith(@"\\", StringComparison.Ordinal) ||
                detectionDirectory.StartsWith(@"\\", StringComparison.Ordinal))
            {
                _logger?.LogWarning("ネットワークパスが検出されました: {ModelsDir}", modelsDirectory);
                return true;
            }
            
            // 空のパスを検出
            if (string.IsNullOrWhiteSpace(modelsDirectory) || string.IsNullOrWhiteSpace(detectionDirectory))
            {
                _logger?.LogWarning("空のパスが検出されました");
                return true;
            }
            
            return false;
        }
        catch (ArgumentException ex)
        {
            _logger?.LogWarning(ex, "パス設定の確認中に引数エラーが発生");
            return true; // エラーが発生した場合は無効とみなす
        }
        catch (NullReferenceException ex)
        {
            _logger?.LogWarning(ex, "パス設定の確認中にnull参照エラーが発生");
            return true;
        }
        catch (NotSupportedException ex)
        {
            _logger?.LogWarning(ex, "パス設定の確認中にサポートされていない操作エラーが発生");
            return true;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger?.LogWarning(ex, "パス設定の確認中にアクセス権限エラーが発生");
            return true;
        }
        catch (System.Security.SecurityException ex)
        {
            _logger?.LogWarning(ex, "パス設定の確認中にセキュリティエラーが発生");
            return true;
        }
        catch (System.IO.IOException ex)
        {
            _logger?.LogWarning(ex, "パス設定の確認中にI/Oエラーが発生");
            return true;
        }
    }

    /// <summary>
    /// 言語コードの妥当性確認
    /// </summary>
    private static bool IsValidLanguage(string language) => language switch
    {
        "eng" or "jpn" => true,
        _ => false
    };

    /// <summary>
    /// 初期化状態のチェック
    /// </summary>
    private void ThrowIfNotInitialized()
    {
        if (!IsInitialized)
        {
            throw new InvalidOperationException("OCRエンジンが初期化されていません。InitializeAsync()を呼び出してください。");
        }
    }

    /// <summary>
    /// 破棄状態のチェック
    /// </summary>
    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
    
    /// <summary>
    /// テスト用のディレクトリ作成シミュレーション
    /// </summary>
    private void CreateTestDirectories()
    {
        try
        {
            // テスト環境でのディレクトリ作成をシミュレート
            var testDirectories = new[]
            {
                _modelPathResolver.GetDetectionModelsDirectory(),
                _modelPathResolver.GetRecognitionModelsDirectory("eng"),
                _modelPathResolver.GetRecognitionModelsDirectory("jpn")
            };
            
            foreach (var directory in testDirectories)
            {
                try
                {
                    _modelPathResolver.EnsureDirectoryExists(directory);
                    _logger?.LogDebug("テスト用ディレクトリ作成: {Directory}", directory);
                }
                catch (ArgumentException ex)
                {
                    _logger?.LogWarning(ex, "テスト用ディレクトリ作成に失敗 - 引数エラー: {Directory}", directory);
                    // テスト環境では継続
                }
                catch (UnauthorizedAccessException ex)
                {
                    _logger?.LogWarning(ex, "テスト用ディレクトリ作成に失敗 - アクセス権限エラー: {Directory}", directory);
                    // テスト環境では継続
                }
                catch (System.IO.IOException ex)
                {
                    _logger?.LogWarning(ex, "テスト用ディレクトリ作成に失敗 - I/Oエラー: {Directory}", directory);
                    // テスト環境では継続
                }
                catch (System.Security.SecurityException ex)
                {
                    _logger?.LogWarning(ex, "テスト用ディレクトリ作成に失敗 - セキュリティエラー: {Directory}", directory);
                    // テスト環境では継続
                }
            }
        }
        catch (ArgumentException ex)
        {
            _logger?.LogError(ex, "テスト用ディレクトリ作成の初期化に失敗 - 引数エラー");
            // テスト環境ではエラーを再スローしない
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger?.LogError(ex, "テスト用ディレクトリ作成の初期化に失敗 - アクセス権限エラー");
            // テスト環境ではエラーを再スローしない
        }
        catch (System.IO.IOException ex)
        {
            _logger?.LogError(ex, "テスト用ディレクトリ作成の初期化に失敗 - I/Oエラー");
            // テスト環境ではエラーを再スローしない
        }
        catch (System.Security.SecurityException ex)
        {
            _logger?.LogError(ex, "テスト用ディレクトリ作成の初期化に失敗 - セキュリティエラー");
            // テスト環境ではエラーを再スローしない
        }
    }

    /// <summary>
    /// リソースの解放
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// リソースの解放（パターン実装）
    /// </summary>
    /// <param name="disposing">マネージドリソースも解放するかどうか</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            _logger?.LogDebug("SafeTestPaddleOcrEngineのリソースを解放中");
            
            IsInitialized = false;
            CurrentLanguage = null;
            IsMultiThreadEnabled = false;
        }

        _disposed = true;
    }
}
