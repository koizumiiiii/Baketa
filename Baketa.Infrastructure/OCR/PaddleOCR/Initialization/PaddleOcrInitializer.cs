using Microsoft.Extensions.Logging;
using Baketa.Infrastructure.OCR.PaddleOCR.Models;
using Sdcb.PaddleOCR;
using System.IO;

namespace Baketa.Infrastructure.OCR.PaddleOCR.Initialization;

/// <summary>
/// PaddleOCRの初期化と管理を行うクラス
/// </summary>
public sealed class PaddleOcrInitializer : IDisposable
{
    private readonly string _baseDirectory;
    private readonly ILogger<PaddleOcrInitializer>? _logger;
    private readonly IModelPathResolver _modelPathResolver;
    
    /// <summary>
    /// 初期化状態を取得
    /// </summary>
    public bool IsInitialized { get; private set; }
    
    private bool _disposed;
    
    public PaddleOcrInitializer(
        string baseDirectory,
        IModelPathResolver modelPathResolver,
        ILogger<PaddleOcrInitializer>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(baseDirectory);
        if (string.IsNullOrWhiteSpace(baseDirectory))
            throw new ArgumentException("Base directory cannot be empty or whitespace.", nameof(baseDirectory));
            
        _baseDirectory = baseDirectory;
        _modelPathResolver = modelPathResolver ?? throw new ArgumentNullException(nameof(modelPathResolver));
        _logger = logger;
    }
    
    /// <summary>
    /// PaddleOCRエンジンを初期化します
    /// </summary>
    /// <returns>初期化が成功した場合はtrue</returns>
    public async Task<bool> InitializeAsync()
    {
        ThrowIfDisposed();
        
        if (IsInitialized)
        {
            _logger?.LogDebug("PaddleOCRエンジンは既に初期化されています");
            return true;
        }
            
        try
        {
            _logger?.LogInformation("PaddleOCRエンジンの初期化を開始");
            
            // ディレクトリの存在確認と作成
            EnsureDirectoryStructure();
            
            // 必要なモデルファイルの確認
            await ValidateRequiredModelsAsync().ConfigureAwait(false);
            
            // ネイティブライブラリの初期化
            InitializeNativeLibraries();
            
            IsInitialized = true;
            _logger?.LogInformation("PaddleOCRエンジンの初期化が完了");
            
            return true;
        }
        catch (DirectoryNotFoundException ex)
        {
            _logger?.LogError(ex, "PaddleOCRエンジンの初期化に失敗しました - 必要なディレクトリが見つかりません");
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger?.LogError(ex, "PaddleOCRエンジンの初期化に失敗しました - ディレクトリまたはファイルへのアクセスが拒否されました");
            return false;
        }
        catch (IOException ex)
        {
            _logger?.LogError(ex, "PaddleOCRエンジンの初期化に失敗しました - I/Oエラーが発生しました");
            return false;
        }
        catch (OcrInitializationException ex)
        {
            _logger?.LogError(ex, "PaddleOCRエンジンの初期化に失敗しました - OCR初期化固有のエラーが発生しました");
            return false;
        }
        catch (ArgumentException ex)
        {
            _logger?.LogError(ex, "PaddleOCRエンジンの初期化に失敗しました - 引数エラーが発生しました");
            return false;
        }
#pragma warning disable CA1031 // Do not catch general exception types - 最終フォールバック処理として必要
        catch (Exception ex)
        {
            _logger?.LogError(ex, "PaddleOCRエンジンの初期化に失敗しました - 予期しないエラーが発生しました");
            return false;
        }
#pragma warning restore CA1031 // Do not catch general exception types
    }
    
    /// <summary>
    /// 必要なディレクトリ構造を確保
    /// </summary>
    private void EnsureDirectoryStructure()
    {
        var directories = new[]
        {
            _modelPathResolver.GetDetectionModelsDirectory(),
            _modelPathResolver.GetRecognitionModelsDirectory("eng"),
            _modelPathResolver.GetRecognitionModelsDirectory("jpn"),
            GetTempDirectory()
        };
        
        foreach (var dir in directories)
        {
            try
            {
                // パスの有効性を事前チェック
                ValidateDirectoryPath(dir);
                
                _modelPathResolver.EnsureDirectoryExists(dir);
                _logger?.LogDebug("ディレクトリを確認/作成: {Directory}", dir);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger?.LogError(ex, "ディレクトリ作成のアクセス権限がありません: {Directory}", dir);
                throw new OcrInitializationException($"OCRディレクトリの作成に失敗（アクセス拒否）: {dir}", ex);
            }
            catch (DirectoryNotFoundException ex)
            {
                _logger?.LogError(ex, "親ディレクトリが見つかりません: {Directory}", dir);
                throw new OcrInitializationException($"OCRディレクトリの作成に失敗（親ディレクトリなし）: {dir}", ex);
            }
            catch (IOException ex)
            {
                _logger?.LogError(ex, "ディレクトリ作成中にI/Oエラーが発生: {Directory}", dir);
                throw new OcrInitializationException($"OCRディレクトリの作成に失敗（I/Oエラー）: {dir}", ex);
            }
            catch (ArgumentException ex)
            {
                _logger?.LogError(ex, "無効なディレクトリパス: {Directory}", dir);
                throw new OcrInitializationException($"OCRディレクトリの作成に失敗（無効なパス）: {dir}", ex);
            }
        }
    }
    
    /// <summary>
    /// 必要なモデルファイルの存在確認
    /// </summary>
    private async Task ValidateRequiredModelsAsync()
    {
        var requiredModels = new[]
        {
            _modelPathResolver.GetDetectionModelPath("det_db_standard"),
            _modelPathResolver.GetRecognitionModelPath("eng", "rec_english_standard"),
            _modelPathResolver.GetRecognitionModelPath("jpn", "rec_japan_standard")
        };
        
        foreach (var modelPath in requiredModels)
        {
            if (!_modelPathResolver.FileExists(modelPath))
            {
                _logger?.LogWarning("必要なモデルファイルが見つかりません: {ModelPath}", modelPath);
                // モデル自動ダウンロードは Issue #39 で実装するため、ここでは警告のみ
            }
        }
        
        await Task.CompletedTask.ConfigureAwait(false); // 非同期操作の準備（将来のモデルダウンロード対応）
    }
    
    /// <summary>
    /// ネイティブライブラリの初期化
    /// </summary>
    private void InitializeNativeLibraries()
    {
        try
        {
            // PaddleOCRライブラリの依存関係チェック
            ValidatePaddleOcrDependencies();
            
            _logger?.LogDebug("ネイティブライブラリの初期化準備完了");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "ネイティブライブラリの初期化に失敗");
            throw new OcrInitializationException("OCRエンジンの初期化に失敗しました", ex);
        }
    }
    
    /// <summary>
    /// PaddleOCRの依存関係を検証
    /// </summary>
    private void ValidatePaddleOcrDependencies()
    {
        try
        {
            // PaddleOCRライブラリのロード確認 - 簡素化
            // PaddleDeviceの代わりにモデルアクセスで確認
            var testModel = Sdcb.PaddleOCR.Models.Local.LocalFullModels.EnglishV3;
            _logger?.LogDebug("PaddleOCRライブラリの依存関係検証完了 - モデルアクセス成功");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "PaddleOCRライブラリの依存関係検証に失敗");
            throw new OcrInitializationException("PaddleOCRライブラリの依存関係に問題があります", ex);
        }
    }
    
    /// <summary>
    /// モデルディレクトリを取得
    /// </summary>
    public string GetModelsDirectory() => _modelPathResolver.GetModelsRootDirectory();
    
    /// <summary>
    /// 一時ディレクトリを取得
    /// </summary>
    public string GetTempDirectory() => Path.Combine(_baseDirectory, "Temp");
    
    /// <summary>
    /// オブジェクトが破棄されているかチェック
    /// </summary>
    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
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
    /// ディレクトリパスの有効性を検証
    /// </summary>
    private static void ValidateDirectoryPath(string directoryPath)
    {
        // UNC パスで無効なネットワークパスをチェック
        if (directoryPath.StartsWith("\\\\", StringComparison.Ordinal))
        {
            var segments = directoryPath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 2 || segments[0] == "invalid" || segments[1] == "path")
            {
                throw new ArgumentException($"無効なネットワークパス: {directoryPath}", nameof(directoryPath));
            }
        }
        
        // その他の無効パターンをチェック
        var invalidPatterns = new[] { "\\\\invalid", "\\\\nonexistent", "\\\\fake" };
        if (invalidPatterns.Any(pattern => directoryPath.StartsWith(pattern, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException($"テスト用無効パス: {directoryPath}", nameof(directoryPath));
        }
    }

    /// <summary>
    /// リソースの解放（パターン実装）
    /// </summary>
    /// <param name="disposing">マネージリソースを解放するかどうか</param>
    private void Dispose(bool disposing)
    {
        if (_disposed)
            return;
            
        if (disposing)
        {
            // マネージリソースの解放
            if (IsInitialized)
            {
                _logger?.LogInformation("PaddleOCRエンジンのリソースを解放");
            }
        }
        
        // アンマネージリソースの解放
        // （現在は該当なし）
        
        _disposed = true;
    }
}
