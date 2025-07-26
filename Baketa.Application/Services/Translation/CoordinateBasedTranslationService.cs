using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Abstractions.UI;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Translation.Models;
using Baketa.Core.Utilities;
using Baketa.Infrastructure.OCR.BatchProcessing;

namespace Baketa.Application.Services.Translation;

/// <summary>
/// 座標ベース翻訳表示サービス
/// バッチOCR処理と複数ウィンドウオーバーレイ表示を統合した座標ベース翻訳システム
/// </summary>
public sealed class CoordinateBasedTranslationService : IDisposable
{
    private readonly IBatchOcrProcessor _batchOcrProcessor;
    private readonly IMultiWindowOverlayManager _overlayManager;
    private readonly ITranslationService _translationService;
    private readonly ILogger<CoordinateBasedTranslationService>? _logger;
    private bool _disposed;

    public CoordinateBasedTranslationService(
        IBatchOcrProcessor batchOcrProcessor,
        IMultiWindowOverlayManager overlayManager,
        ITranslationService translationService,
        ILogger<CoordinateBasedTranslationService>? logger = null)
    {
        _batchOcrProcessor = batchOcrProcessor ?? throw new ArgumentNullException(nameof(batchOcrProcessor));
        _overlayManager = overlayManager ?? throw new ArgumentNullException(nameof(overlayManager));
        _translationService = translationService ?? throw new ArgumentNullException(nameof(translationService));
        _logger = logger;
        
        _logger?.LogInformation("🚀 CoordinateBasedTranslationService initialized");
    }

    /// <summary>
    /// 座標ベース翻訳処理を実行
    /// バッチOCR処理 → 複数ウィンドウオーバーレイ表示の統合フロー
    /// </summary>
    public async Task ProcessWithCoordinateBasedTranslationAsync(
        IAdvancedImage image, 
        IntPtr windowHandle,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        try
        {
            _logger?.LogInformation("🎯 座標ベース翻訳処理開始 - 画像: {Width}x{Height}, ウィンドウ: 0x{Handle:X}", 
                image.Width, image.Height, windowHandle.ToInt64());
            DebugLogUtility.WriteLog($"🎯 座標ベース翻訳処理開始 - 画像: {image.Width}x{image.Height}, ウィンドウ: 0x{windowHandle.ToInt64():X}");

            // バッチOCR処理でテキストチャンクを取得
            _logger?.LogDebug("📦 バッチOCR処理開始");
            DebugLogUtility.WriteLog("📦 バッチOCR処理開始");
            var textChunks = await _batchOcrProcessor.ProcessBatchAsync(image, windowHandle, cancellationToken)
                .ConfigureAwait(false);
            
            _logger?.LogInformation("✅ バッチOCR完了 - チャンク数: {ChunkCount}", textChunks.Count);
            DebugLogUtility.WriteLog($"✅ バッチOCR完了 - チャンク数: {textChunks.Count}");
            
            // チャンクの詳細情報をデバッグ出力
            for (int i = 0; i < textChunks.Count; i++)
            {
                var chunk = textChunks[i];
                System.Console.WriteLine($"📍 チャンク[{i}] ID={chunk.ChunkId}, 位置=({chunk.CombinedBounds.X},{chunk.CombinedBounds.Y}), サイズ=({chunk.CombinedBounds.Width}x{chunk.CombinedBounds.Height}), テキスト='{chunk.CombinedText}'");
            }

            if (textChunks.Count == 0)
            {
                _logger?.LogWarning("📝 テキストチャンクが0個のため、オーバーレイ表示をスキップ");
                DebugLogUtility.WriteLog("📝 テキストチャンクが0個のため、オーバーレイ表示をスキップ");
                return;
            }

            // デバッグ用: 翻訳をスキップしてOCRテキストをそのまま表示
            _logger?.LogDebug("🔧 デバッグモード: OCRテキストをそのまま表示");
            DebugLogUtility.WriteLog($"🔧 デバッグモード: OCRテキストをそのまま表示 - チャンク数: {textChunks.Count}");
            
            foreach (var chunk in textChunks)
            {
                // OCRテキストをそのまま翻訳結果として設定（デバッグ用）
                chunk.TranslatedText = chunk.CombinedText;
                
                _logger?.LogDebug("📝 OCRテキスト表示 - ChunkId: {ChunkId}, テキスト: '{Text}'", 
                    chunk.ChunkId, chunk.CombinedText);
            }
            
            /* 翻訳処理は一時的にコメントアウト
            foreach (var chunk in textChunks)
            {
                try
                {
                    // 空のテキストはスキップ
                    if (string.IsNullOrWhiteSpace(chunk.CombinedText))
                    {
                        chunk.TranslatedText = "";
                        continue;
                    }
                    
                    // 実際の翻訳サービスで翻訳実行
                    var translationResult = await _translationService.TranslateAsync(
                        chunk.CombinedText, 
                        Language.Japanese, 
                        Language.English, 
                        null,
                        cancellationToken).ConfigureAwait(false);
                        
                    chunk.TranslatedText = translationResult.TranslatedText ?? string.Empty;
                    
                    _logger?.LogDebug("🌐 翻訳完了 - ChunkId: {ChunkId}, 原文: '{Original}', 翻訳: '{Translated}'", 
                        chunk.ChunkId, chunk.CombinedText, chunk.TranslatedText);
                }
                catch (Exception ex)
                {
                    // 翻訳エラー時はフォールバック
                    _logger?.LogWarning(ex, "⚠️ 翻訳エラー - ChunkId: {ChunkId}, フォールバック表示", chunk.ChunkId);
                    chunk.TranslatedText = $"[翻詳エラー] {chunk.CombinedText}";
                }
            }
            */
            
            _logger?.LogInformation("✅ 翻訳処理完了 - 処理チャンク数: {Count}, 成功チャンク数: {SuccessCount}", 
                textChunks.Count, textChunks.Count(c => !string.IsNullOrEmpty(c.TranslatedText) && !c.TranslatedText.StartsWith("[翻訳エラー]", StringComparison.Ordinal)));

            // 複数ウィンドウオーバーレイで表示
            _logger?.LogDebug("🖼️ 複数ウィンドウオーバーレイ表示開始");
            DebugLogUtility.WriteLog("🖼️ 複数ウィンドウオーバーレイ表示開始");
            
            try
            {
                DebugLogUtility.WriteLog($"🔥🔥🔥 DisplayTranslationResultsAsync呼び出し直前 - _overlayManager null?: {_overlayManager == null}");
                if (_overlayManager != null)
                {
                    await _overlayManager.DisplayTranslationResultsAsync(textChunks, cancellationToken)
                        .ConfigureAwait(false);
                }
                DebugLogUtility.WriteLog("🔥🔥🔥 DisplayTranslationResultsAsync呼び出し直後");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "❌ DisplayTranslationResultsAsync呼び出しでエラー");
                DebugLogUtility.WriteLog($"❌❌❌ DisplayTranslationResultsAsync呼び出しエラー: {ex.GetType().Name} - {ex.Message}");
                DebugLogUtility.WriteLog($"❌❌❌ スタックトレース: {ex.StackTrace}");
                throw;
            }
            
            _logger?.LogInformation("🎉 座標ベース翻訳処理完了 - 座標ベース翻訳表示成功");
            DebugLogUtility.WriteLog("🎉 座標ベース翻訳処理完了 - 座標ベース翻訳表示成功");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "❌ 座標ベース翻訳処理でエラーが発生しました");
            throw;
        }
    }

    /// <summary>
    /// 座標ベース翻訳システムが利用可能かどうかを確認
    /// </summary>
    public bool IsCoordinateBasedTranslationAvailable()
    {
        ThrowIfDisposed();
        
        try
        {
            var available = _batchOcrProcessor != null && _overlayManager != null;
            _logger?.LogDebug("🔍 座標ベース翻訳システム可用性チェック: {Available}", available);
            return available;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "⚠️ 座標ベース翻訳システム可用性チェックでエラー");
            return false;
        }
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
            // MultiWindowOverlayManagerのクリーンアップ
            if (_overlayManager is IDisposable disposableOverlayManager)
            {
                disposableOverlayManager.Dispose();
            }

            // BatchOcrProcessorのクリーンアップ
            if (_batchOcrProcessor is IDisposable disposableBatchProcessor)
            {
                disposableBatchProcessor.Dispose();
            }

            _disposed = true;
            _logger?.LogInformation("🧹 CoordinateBasedTranslationService disposed");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "❌ CoordinateBasedTranslationService dispose error");
        }
    }
}