using System.Drawing;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Models.OCR;

namespace Baketa.Application.Services.OCR;

/// <summary>
/// OCRサービスのアプリケーション層実装
/// </summary>
public interface IOcrApplicationService
{
    /// <summary>
    /// OCRエンジンが利用可能かどうか
    /// </summary>
    bool IsAvailable { get; }
    
    /// <summary>
    /// 現在の言語設定
    /// </summary>
    string? CurrentLanguage { get; }
    
    /// <summary>
    /// OCRサービスを初期化
    /// </summary>
    /// <param name="language">言語コード（省略時は日本語）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>初期化が成功した場合はtrue</returns>
    Task<bool> InitializeAsync(string language = "jpn", CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 画像からテキストを認識
    /// </summary>
    /// <param name="image">認識対象の画像</param>
    /// <param name="progressCallback">進捗通知コールバック</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>OCR結果</returns>
    Task<OcrResults> RecognizeTextAsync(
        IImage image,
        IProgress<OcrProgress>? progressCallback = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 画像の指定領域からテキストを認識（ROI指定）
    /// </summary>
    /// <param name="image">認識対象の画像</param>
    /// <param name="regionOfInterest">関心領域</param>
    /// <param name="progressCallback">進捗通知コールバック</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>OCR結果</returns>
    Task<OcrResults> RecognizeTextAsync(
        IImage image,
        Rectangle regionOfInterest,
        IProgress<OcrProgress>? progressCallback = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 言語を変更
    /// </summary>
    /// <param name="language">新しい言語コード</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>変更が成功した場合はtrue</returns>
    Task<bool> SwitchLanguageAsync(string language, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 利用可能な言語一覧を取得
    /// </summary>
    /// <returns>言語コードのリスト</returns>
    IReadOnlyList<string> GetAvailableLanguages();
    
    /// <summary>
    /// 指定言語のモデルが利用可能かチェック
    /// </summary>
    /// <param name="language">言語コード</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>利用可能な場合はtrue</returns>
    Task<bool> IsLanguageAvailableAsync(string language, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// OCRエンジンの設定を取得
    /// </summary>
    /// <returns>現在の設定</returns>
    OcrEngineSettings GetSettings();
    
    /// <summary>
    /// OCRエンジンの設定を適用
    /// </summary>
    /// <param name="settings">新しい設定</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    Task ApplySettingsAsync(OcrEngineSettings settings, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// パフォーマンス統計を取得
    /// </summary>
    /// <returns>パフォーマンス統計</returns>
    OcrPerformanceStats GetPerformanceStats();
}

/// <summary>
/// OCRアプリケーションサービスの実装
/// </summary>
public sealed class OcrApplicationService(
    IOcrEngine ocrEngine,
    IOcrModelManager modelManager,
    ILogger<OcrApplicationService>? logger = null) : IOcrApplicationService, IDisposable
{
    private readonly IOcrEngine _ocrEngine = ocrEngine ?? throw new ArgumentNullException(nameof(ocrEngine));
    private readonly IOcrModelManager _modelManager = modelManager ?? throw new ArgumentNullException(nameof(modelManager));
    private readonly ILogger<OcrApplicationService>? _logger = logger;
    private bool _disposed;

    public bool IsAvailable => _ocrEngine.IsInitialized;
    public string? CurrentLanguage => _ocrEngine.CurrentLanguage;

    /// <summary>
    /// OCRサービスを初期化
    /// </summary>
    public async Task<bool> InitializeAsync(string language = "jpn", CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        try
        {
            _logger?.LogInformation("OCRサービスの初期化を開始: 言語={Language}", language);

            // 必要なモデルがダウンロード済みかチェック
            if (!await _modelManager.IsLanguageCompleteAsync(language, cancellationToken).ConfigureAwait(false))
            {
                _logger?.LogWarning("言語 {Language} の必要なモデルが不足しています", language);
                
                // 必要なモデルを自動ダウンロード
                var modelsForLanguage = await _modelManager.GetModelsForLanguageAsync(language, cancellationToken).ConfigureAwait(false);
                var requiredModels = modelsForLanguage.Where(m => m.IsRequired).ToList();
                
                if (requiredModels.Count > 0)
                {
                    _logger?.LogInformation("必要なモデルを自動ダウンロード中: {Count}個", requiredModels.Count);
                    
                    var downloadProgress = new Progress<ModelDownloadProgress>(progress =>
                    {
                        _logger?.LogDebug("モデルダウンロード進捗: {ModelName} - {Progress:P0}", 
                            progress.ModelInfo.Name, progress.Progress);
                    });
                    
                    var downloadSuccess = await _modelManager.DownloadModelsAsync(
                        requiredModels, downloadProgress, cancellationToken).ConfigureAwait(false);
                    
                    if (!downloadSuccess)
                    {
                        _logger?.LogError("必要なモデルのダウンロードに失敗しました");
                        return false;
                    }
                }
            }

            // OCRエンジンの初期化
            var settings = new OcrEngineSettings { Language = language };
            var success = await _ocrEngine.InitializeAsync(settings, cancellationToken).ConfigureAwait(false);
            
            if (success)
            {
                _logger?.LogInformation("OCRサービスの初期化が完了しました");
            }
            else
            {
                _logger?.LogError("OCRエンジンの初期化に失敗しました");
            }
            
            return success;
        }
        catch (ModelManagementException ex)
        {
            _logger?.LogError(ex, "OCRサービスの初期化中にモデル管理エラーが発生しました");
            return false;
        }
        catch (HttpRequestException ex)
        {
            _logger?.LogError(ex, "OCRサービスの初期化中にネットワークエラーが発生しました");
            return false;
        }
        catch (OperationCanceledException)
        {
            _logger?.LogInformation("OCRサービスの初期化がキャンセルされました");
            throw;
        }
    }

    /// <summary>
    /// 画像からテキストを認識
    /// </summary>
    public async Task<OcrResults> RecognizeTextAsync(
        IImage image,
        IProgress<OcrProgress>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(image);
        
        if (!IsAvailable)
        {
            throw new InvalidOperationException("OCRサービスが初期化されていません。InitializeAsync()を先に呼び出してください。");
        }

        try
        {
            _logger?.LogDebug("OCR認識を開始: 画像サイズ={Width}x{Height}", image.Width, image.Height);
            
            var result = await _ocrEngine.RecognizeAsync(image, progressCallback, cancellationToken).ConfigureAwait(false);
            
            _logger?.LogDebug("OCR認識完了: テキスト領域数={Count}, 処理時間={ElapsedMs}ms", 
                result.TextRegions.Count, result.ProcessingTime.TotalMilliseconds);
            
            return result;
        }
        catch (OcrException ex)
        {
            _logger?.LogError(ex, "OCR認識中にエラーが発生しました");
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger?.LogInformation("OCR認識がキャンセルされました");
            throw;
        }
    }

    /// <summary>
    /// 画像の指定領域からテキストを認識（ROI指定）
    /// </summary>
    public async Task<OcrResults> RecognizeTextAsync(
        IImage image,
        Rectangle regionOfInterest,
        IProgress<OcrProgress>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(image);
        
        if (!IsAvailable)
        {
            throw new InvalidOperationException("OCRサービスが初期化されていません。InitializeAsync()を先に呼び出してください。");
        }

        try
        {
            _logger?.LogDebug("ROI指定OCR認識を開始: 画像サイズ={Width}x{Height}, ROI={ROI}", 
                image.Width, image.Height, regionOfInterest);
            
            var result = await _ocrEngine.RecognizeAsync(image, regionOfInterest, progressCallback, cancellationToken).ConfigureAwait(false);
            
            _logger?.LogDebug("ROI指定OCR認識完了: テキスト領域数={Count}, 処理時間={ElapsedMs}ms", 
                result.TextRegions.Count, result.ProcessingTime.TotalMilliseconds);
            
            return result;
        }
        catch (OcrException ex)
        {
            _logger?.LogError(ex, "ROI指定OCR認識中にエラーが発生しました");
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger?.LogInformation("ROI指定OCR認識がキャンセルされました");
            throw;
        }
    }

    /// <summary>
    /// 言語を変更
    /// </summary>
    public async Task<bool> SwitchLanguageAsync(string language, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        if (string.IsNullOrWhiteSpace(language))
        {
            throw new ArgumentException("言語コードが無効です", nameof(language));
        }

        if (CurrentLanguage == language)
        {
            _logger?.LogDebug("既に指定された言語に設定されています: {Language}", language);
            return true;
        }

        try
        {
            _logger?.LogInformation("言語切り替えを開始: {OldLanguage} -> {NewLanguage}", CurrentLanguage, language);

            // 新しい言語のモデルが利用可能かチェック
            if (!await IsLanguageAvailableAsync(language, cancellationToken).ConfigureAwait(false))
            {
                _logger?.LogWarning("言語 {Language} のモデルが利用できません", language);
                return false;
            }

            // 設定を更新
            var currentSettings = _ocrEngine.GetSettings();
            currentSettings.Language = language;
            
            await _ocrEngine.ApplySettingsAsync(currentSettings, cancellationToken).ConfigureAwait(false);
            
            _logger?.LogInformation("言語切り替えが完了しました: {Language}", language);
            return true;
        }
        catch (OcrException ex)
        {
            _logger?.LogError(ex, "言語切り替え中にOCRエラーが発生しました: {Language}", language);
            return false;
        }
        catch (OperationCanceledException)
        {
            _logger?.LogInformation("言語切り替えがキャンセルされました: {Language}", language);
            throw;
        }
    }

    /// <summary>
    /// 利用可能な言語一覧を取得
    /// </summary>
    public IReadOnlyList<string> GetAvailableLanguages()
    {
        ThrowIfDisposed();
        return _ocrEngine.GetAvailableLanguages();
    }

    /// <summary>
    /// 指定言語のモデルが利用可能かチェック
    /// </summary>
    public async Task<bool> IsLanguageAvailableAsync(string language, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        if (string.IsNullOrWhiteSpace(language))
            return false;

        try
        {
            // エンジンでサポートされている言語かチェック
            var availableLanguages = GetAvailableLanguages();
            if (!availableLanguages.Contains(language))
                return false;

            // 必要なモデルがすべて揃っているかチェック
            return await _modelManager.IsLanguageCompleteAsync(language, cancellationToken).ConfigureAwait(false);
        }
        catch (ModelManagementException ex)
        {
            _logger?.LogError(ex, "言語利用可能性チェック中にモデル管理エラー: {Language}", language);
            return false;
        }
        catch (HttpRequestException ex)
        {
            _logger?.LogError(ex, "言語利用可能性チェック中にネットワークエラー: {Language}", language);
            return false;
        }
    }

    /// <summary>
    /// OCRエンジンの設定を取得
    /// </summary>
    public OcrEngineSettings GetSettings()
    {
        ThrowIfDisposed();
        return _ocrEngine.GetSettings();
    }

    /// <summary>
    /// OCRエンジンの設定を適用
    /// </summary>
    public async Task ApplySettingsAsync(OcrEngineSettings settings, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            _logger?.LogDebug("OCR設定を適用中: 言語={Language}, GPU={UseGpu}", settings.Language, settings.UseGpu);
            
            await _ocrEngine.ApplySettingsAsync(settings, cancellationToken).ConfigureAwait(false);
            
            _logger?.LogInformation("OCR設定の適用が完了しました");
        }
        catch (OcrException ex)
        {
            _logger?.LogError(ex, "OCR設定の適用中にOCRエラーが発生しました");
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger?.LogInformation("OCR設定の適用がキャンセルされました");
            throw;
        }
    }

    /// <summary>
    /// パフォーマンス統計を取得
    /// </summary>
    public OcrPerformanceStats GetPerformanceStats()
    {
        ThrowIfDisposed();
        return _ocrEngine.GetPerformanceStats();
    }

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
    /// <param name="disposing">マネージドリソースを解放するか</param>
    private void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _logger?.LogDebug("OcrApplicationServiceのリソースを解放中");
                _ocrEngine?.Dispose();
            }
            _disposed = true;
        }
    }

    /// <summary>
    /// リソースの解放
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
