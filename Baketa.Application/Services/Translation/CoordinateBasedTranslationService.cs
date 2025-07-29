using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CoordinateBasedTranslationService>? _logger;
    private bool _disposed;

    public CoordinateBasedTranslationService(
        IBatchOcrProcessor batchOcrProcessor,
        IMultiWindowOverlayManager overlayManager,
        ITranslationService translationService,
        IServiceProvider serviceProvider,
        ILogger<CoordinateBasedTranslationService>? logger = null)
    {
        _batchOcrProcessor = batchOcrProcessor ?? throw new ArgumentNullException(nameof(batchOcrProcessor));
        _overlayManager = overlayManager ?? throw new ArgumentNullException(nameof(overlayManager));
        _translationService = translationService ?? throw new ArgumentNullException(nameof(translationService));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
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
            DebugLogUtility.WriteLog($"\n🔍 [CoordinateBasedTranslationService] バッチOCR結果詳細解析 (ウィンドウ: 0x{windowHandle.ToInt64():X}):");
            DebugLogUtility.WriteLog($"   入力画像サイズ: {image.Width}x{image.Height}");
            DebugLogUtility.WriteLog($"   検出されたテキストチャンク数: {textChunks.Count}");
            
            for (int i = 0; i < textChunks.Count; i++)
            {
                var chunk = textChunks[i];
                DebugLogUtility.WriteLog($"\n📍 チャンク[{i}] ID={chunk.ChunkId}");
                DebugLogUtility.WriteLog($"   OCR生座標: X={chunk.CombinedBounds.X}, Y={chunk.CombinedBounds.Y}");
                DebugLogUtility.WriteLog($"   OCR生サイズ: W={chunk.CombinedBounds.Width}, H={chunk.CombinedBounds.Height}");
                DebugLogUtility.WriteLog($"   元テキスト: '{chunk.CombinedText}'");
                DebugLogUtility.WriteLog($"   翻訳テキスト: '{chunk.TranslatedText}'");
                
                // 座標変換情報
                var arPos = chunk.GetARPosition();
                var arSize = chunk.GetARSize();
                DebugLogUtility.WriteLog($"   AR位置: ({arPos.X},{arPos.Y}) [元座標と同じ]");
                DebugLogUtility.WriteLog($"   ARサイズ: ({arSize.Width},{arSize.Height}) [元サイズと同じ]");
                DebugLogUtility.WriteLog($"   計算フォントサイズ: {chunk.CalculateARFontSize()}px (Height {chunk.CombinedBounds.Height} * 0.45)");
                DebugLogUtility.WriteLog($"   AR表示可能: {chunk.CanShowAR()}");
                
                // TextResultsの詳細情報
                DebugLogUtility.WriteLog($"   構成TextResults数: {chunk.TextResults.Count}");
                for (int j = 0; j < Math.Min(chunk.TextResults.Count, 3); j++) // 最初の3個だけ表示
                {
                    var result = chunk.TextResults[j];
                    DebugLogUtility.WriteLog($"     [{j}] テキスト: '{result.Text}', 位置: ({result.BoundingBox.X},{result.BoundingBox.Y}), サイズ: ({result.BoundingBox.Width}x{result.BoundingBox.Height})");
                }
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

            // AR風オーバーレイ表示を優先的に使用
            var arOverlayManager = _serviceProvider.GetService<IARTranslationOverlayManager>();
            if (arOverlayManager != null)
            {
                _logger?.LogInformation("🎯 AR風オーバーレイ表示開始 - チャンク数: {Count}", textChunks.Count);
                DebugLogUtility.WriteLog($"🎯 AR風オーバーレイ表示開始 - チャンク数: {textChunks.Count}");
                
                try
                {
                    // AR翻訳オーバーレイマネージャーを初期化
                    await arOverlayManager.InitializeAsync().ConfigureAwait(false);
                    
                    // 各テキストチャンクをAR風で表示
                    DebugLogUtility.WriteLog($"\n🎭 AR表示開始処理:");
                    foreach (var chunk in textChunks)
                    {
                        DebugLogUtility.WriteLog($"\n🔸 チャンク {chunk.ChunkId} AR表示判定:");
                        DebugLogUtility.WriteLog($"   AR表示可能: {chunk.CanShowAR()}");
                        DebugLogUtility.WriteLog($"   元座標: ({chunk.CombinedBounds.X},{chunk.CombinedBounds.Y})");
                        DebugLogUtility.WriteLog($"   元サイズ: ({chunk.CombinedBounds.Width},{chunk.CombinedBounds.Height})");
                        
                        if (chunk.CanShowAR())
                        {
                            _logger?.LogDebug("🎭 AR表示 - ChunkId: {ChunkId}, 位置: ({X},{Y}), サイズ: ({W}x{H})", 
                                chunk.ChunkId, chunk.CombinedBounds.X, chunk.CombinedBounds.Y, 
                                chunk.CombinedBounds.Width, chunk.CombinedBounds.Height);
                            
                            await arOverlayManager.ShowAROverlayAsync(chunk, cancellationToken)
                                .ConfigureAwait(false);
                            
                            DebugLogUtility.WriteLog($"   ✅ AR表示完了 - チャンク {chunk.ChunkId}");
                        }
                        else
                        {
                            _logger?.LogWarning("⚠️ AR表示条件を満たしていません - {ARLog}", chunk.ToARLogString());
                            DebugLogUtility.WriteLog($"   ❌ AR表示スキップ - チャンク {chunk.ChunkId}: 条件未満足");
                        }
                    }
                    
                    _logger?.LogInformation("✅ AR風オーバーレイ表示完了 - アクティブオーバーレイ数: {Count}", 
                        arOverlayManager.ActiveOverlayCount);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "❌ AR風オーバーレイ表示でエラーが発生");
                    DebugLogUtility.WriteLog($"❌❌❌ AR風オーバーレイエラー: {ex.GetType().Name} - {ex.Message}");
                    
                    // AR風UIでエラーが発生した場合は従来のオーバーレイにフォールバック
                    _logger?.LogWarning("🔄 従来のオーバーレイ表示にフォールバック");
                    await FallbackToTraditionalOverlay(textChunks, cancellationToken).ConfigureAwait(false);
                }
            }
            else
            {
                // AR風オーバーレイが利用できない場合は従来のオーバーレイを使用
                _logger?.LogWarning("⚠️ AR風オーバーレイが利用できません。従来のオーバーレイを使用");
                await FallbackToTraditionalOverlay(textChunks, cancellationToken).ConfigureAwait(false);
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
    /// 従来のオーバーレイ表示にフォールバック
    /// </summary>
    private async Task FallbackToTraditionalOverlay(
        IReadOnlyList<TextChunk> textChunks, 
        CancellationToken cancellationToken)
    {
        try
        {
            _logger?.LogDebug("🖼️ 従来のオーバーレイ表示開始");
            DebugLogUtility.WriteLog("🖼️ 従来のオーバーレイ表示開始");
            
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
            _logger?.LogError(ex, "❌ 従来のオーバーレイ表示でもエラーが発生");
            DebugLogUtility.WriteLog($"❌❌❌ 従来のオーバーレイエラー: {ex.GetType().Name} - {ex.Message}");
            DebugLogUtility.WriteLog($"❌❌❌ スタックトレース: {ex.StackTrace}");
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
            var batchOcrAvailable = _batchOcrProcessor != null;
            var overlayAvailable = _overlayManager != null;
            var available = batchOcrAvailable && overlayAvailable;
            
            DebugLogUtility.WriteLog($"🔍 [CoordinateBasedTranslationService] 座標ベース翻訳システム可用性チェック:");
            DebugLogUtility.WriteLog($"   📦 BatchOcrProcessor: {batchOcrAvailable}");
            DebugLogUtility.WriteLog($"   🖼️ OverlayManager: {overlayAvailable}");
            DebugLogUtility.WriteLog($"   ✅ 総合判定: {available}");
            
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