using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Imaging;

namespace Baketa.Application.Services;

/// <summary>
/// Gemini推奨の段階的OCR戦略実装
/// 高速エンジン（軽量）→ 高精度エンジン（重い）の段階的切り替え
/// </summary>
public sealed class CompositeOcrEngine(
    ILogger<CompositeOcrEngine> logger,
    IOcrEngine fastEngine,
    OcrEngineInitializerService heavyEngineService) : IOcrEngine
{
    private readonly ILogger<CompositeOcrEngine> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly IOcrEngine _fastEngine = fastEngine ?? throw new ArgumentNullException(nameof(fastEngine));          // SafePaddleOcrEngine（5ms初期化）
    private readonly OcrEngineInitializerService _heavyEngineService = heavyEngineService ?? throw new ArgumentNullException(nameof(heavyEngineService)); // バックグラウンド初期化中の重いエンジン
    private bool _disposed;

    public string EngineName => "Composite OCR Engine (Fast→Heavy)";
    public string EngineVersion => "1.0.0 (Gemini Strategy)";
    public bool IsInitialized => _fastEngine.IsInitialized || _heavyEngineService.IsInitialized;
    public string? CurrentLanguage => GetActiveEngine()?.CurrentLanguage;

    /// <summary>
    /// 段階的初期化：まず高速エンジンを初期化し、重いエンジンはバックグラウンド処理に任せる
    /// </summary>
    public async Task<bool> InitializeAsync(OcrEngineSettings? settings = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("🚀 CompositeOcrEngine初期化開始 - 段階的戦略");
        
        // まず高速エンジンを即座に初期化（5ms）
        var fastInitialized = await _fastEngine.InitializeAsync(settings, cancellationToken);
        
        if (fastInitialized)
        {
            _logger.LogInformation("⚡ 高速OCRエンジン初期化完了 - 即座に利用可能");
        }
        
        // 重いエンジンの状態をログ出力
        if (_heavyEngineService.IsInitialized)
        {
            _logger.LogInformation("✅ 高精度OCRエンジンは既に初期化済み");
        }
        else if (_heavyEngineService.IsInitializing)
        {
            _logger.LogInformation("🔄 高精度OCRエンジンはバックグラウンドで初期化中");
        }
        else
        {
            _logger.LogInformation("⏳ 高精度OCRエンジンはまだ初期化されていません");
        }
        
        return fastInitialized; // 高速エンジンが利用可能なら初期化成功
    }
    
    public async Task<bool> WarmupAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("🔥 CompositeOcrEngineウォームアップ開始");
        
        // 高精度エンジンが初期化済みの場合のみウォームアップ
        var heavyEngine = _heavyEngineService.GetInitializedEngine();
        if (heavyEngine != null)
        {
            try
            {
                var result = await heavyEngine.WarmupAsync(cancellationToken);
                _logger.LogInformation($"✅ 高精度OCRエンジンウォームアップ結果: {result}");
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ 高精度OCRエンジンウォームアップ中にエラー");
                return false;
            }
        }
        else
        {
            _logger.LogInformation("⏳ 高精度OCRエンジンは未初期化のためウォームアップをスキップ");
            return false;
        }
    }

    /// <summary>
    /// Gemini推奨の段階的OCR処理（ROI指定）
    /// 高精度エンジンが準備完了ならそれを使用、そうでなければ高速エンジンを使用
    /// </summary>
    public async Task<OcrResults> RecognizeAsync(
        IImage image, 
        Rectangle? regionOfInterest, 
        IProgress<OcrProgress>? progressCallback = null, 
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        var activeEngine = GetActiveEngine();
        if (activeEngine == null)
        {
            throw new InvalidOperationException("利用可能なOCRエンジンがありません。InitializeAsync()を先に呼び出してください。");
        }
        
        var engineType = activeEngine == _fastEngine ? "高速エンジン" : "高精度エンジン";
        _logger.LogDebug("🔍 OCR処理実行: {EngineType}を使用", engineType);
        
        var result = await activeEngine.RecognizeAsync(image, regionOfInterest, progressCallback, cancellationToken);
        
        // 結果の品質評価（将来の拡張用）
        if (ShouldUseFastResult(result, activeEngine))
        {
            _logger.LogDebug("✅ {EngineType}の結果で十分", engineType);
            return result;
        }
        else if (activeEngine == _fastEngine && _heavyEngineService.IsInitialized)
        {
            _logger.LogInformation("🔄 高速エンジンの結果が不十分 - 高精度エンジンで再処理");
            var heavyEngine = _heavyEngineService.GetInitializedEngine();
            if (heavyEngine != null)
            {
                return await heavyEngine.RecognizeAsync(image, regionOfInterest, progressCallback, cancellationToken);
            }
        }
        
        return result;
    }

    /// <summary>
    /// Gemini推奨の段階的OCR処理（ROIなし）
    /// </summary>
    public async Task<OcrResults> RecognizeAsync(
        IImage image,
        IProgress<OcrProgress>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        return await RecognizeAsync(image, null, progressCallback, cancellationToken);
    }

    /// <summary>
    /// 現在利用可能な最適なエンジンを取得
    /// 優先順位: 高精度エンジン（準備完了時） → 高速エンジン
    /// </summary>
    private IOcrEngine? GetActiveEngine()
    {
        // 高精度エンジンが利用可能ならそれを優先
        if (_heavyEngineService.IsInitialized)
        {
            var heavyEngine = _heavyEngineService.GetInitializedEngine();
            if (heavyEngine != null)
            {
                return heavyEngine;
            }
        }
        
        // フォールバック: 高速エンジン
        return _fastEngine.IsInitialized ? _fastEngine : null;
    }

    /// <summary>
    /// 高速エンジンの結果で十分かどうかを判定
    /// 将来の拡張: 文字数、言語、信頼度などの条件
    /// </summary>
    private static bool ShouldUseFastResult(OcrResults result, IOcrEngine usedEngine)
    {
        // 現在は高精度エンジンの結果は常に採用
        // 高速エンジンの結果は後から高精度エンジンで再処理する可能性がある
        return false; // 常に高精度エンジンが利用可能なら切り替える
    }

    // IOcrEngineインターフェースの他のメンバー実装
    public async Task ApplySettingsAsync(OcrEngineSettings settings, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var activeEngine = GetActiveEngine();
        if (activeEngine != null)
        {
            await activeEngine.ApplySettingsAsync(settings, cancellationToken);
        }
    }

    public OcrEngineSettings GetSettings()
    {
        ThrowIfDisposed();
        return GetActiveEngine()?.GetSettings() ?? new OcrEngineSettings();
    }

    public IReadOnlyList<string> GetAvailableLanguages()
    {
        ThrowIfDisposed();
        return GetActiveEngine()?.GetAvailableLanguages() ?? [];
    }

    public IReadOnlyList<string> GetAvailableModels()
    {
        ThrowIfDisposed();
        return GetActiveEngine()?.GetAvailableModels() ?? [];
    }

    public async Task<bool> IsLanguageAvailableAsync(string languageCode, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var activeEngine = GetActiveEngine();
        return activeEngine != null && await activeEngine.IsLanguageAvailableAsync(languageCode, cancellationToken);
    }

    public OcrPerformanceStats GetPerformanceStats()
    {
        ThrowIfDisposed();
        return GetActiveEngine()?.GetPerformanceStats() ?? new OcrPerformanceStats();
    }

    public void CancelCurrentOcrTimeout()
    {
        ThrowIfDisposed();
        GetActiveEngine()?.CancelCurrentOcrTimeout();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        try
        {
            _fastEngine?.Dispose();
            // heavyEngineServiceは別途管理されているため、ここでは破棄しない
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CompositeOcrEngine破棄時にエラーが発生");
        }
        
        _disposed = true;
    }
}
