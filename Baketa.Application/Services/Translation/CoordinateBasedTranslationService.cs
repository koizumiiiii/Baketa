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
    private readonly IInPlaceTranslationOverlayManager _overlayManager;
    private readonly ITranslationService _translationService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CoordinateBasedTranslationService>? _logger;
    private bool _disposed;

    public CoordinateBasedTranslationService(
        IBatchOcrProcessor batchOcrProcessor,
        IInPlaceTranslationOverlayManager overlayManager,
        ITranslationService translationService,
        IServiceProvider serviceProvider,
        ILogger<CoordinateBasedTranslationService>? logger = null)
    {
        _batchOcrProcessor = batchOcrProcessor ?? throw new ArgumentNullException(nameof(batchOcrProcessor));
        _overlayManager = overlayManager ?? throw new ArgumentNullException(nameof(overlayManager));
        _translationService = translationService ?? throw new ArgumentNullException(nameof(translationService));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger;
        
        _logger?.LogInformation("🚀 CoordinateBasedTranslationService initialized - Hash: {Hash}", this.GetHashCode());
        
        // 直接書き込みで初期化ログ
        try
        {
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚀 [DEBUG] CoordinateBasedTranslationService作成: Hash={this.GetHashCode()}{Environment.NewLine}");
        }
        catch { }
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
        // 直接書き込みでメソッド開始をログ
        try
        {
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔄 ProcessWithCoordinateBasedTranslationAsync内部開始（直接書き込み）{Environment.NewLine}");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔍 [DEBUG] CoordinateBasedTranslationService状態: _disposed={_disposed}{Environment.NewLine}");
        }
        catch { }
        
        ThrowIfDisposed();
        
        try
        {
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🎯 座標ベース翻訳処理開始（直接書き込み） - 画像: {image.Width}x{image.Height}, ウィンドウ: 0x{windowHandle.ToInt64():X}{Environment.NewLine}");
        }
        catch { }
        
        try
        {
            _logger?.LogInformation("🎯 座標ベース翻訳処理開始 - 画像: {Width}x{Height}, ウィンドウ: 0x{Handle:X}", 
                image.Width, image.Height, windowHandle.ToInt64());
            DebugLogUtility.WriteLog($"🎯 座標ベース翻訳処理開始 - 画像: {image.Width}x{image.Height}, ウィンドウ: 0x{windowHandle.ToInt64():X}");

            // バッチOCR処理でテキストチャンクを取得
            _logger?.LogDebug("📦 バッチOCR処理開始");
            DebugLogUtility.WriteLog("📦 バッチOCR処理開始");
            
            try
            {
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 📦 バッチOCR処理呼び出し直前（直接書き込み）{Environment.NewLine}");
            }
            catch { }
            
            var textChunks = await _batchOcrProcessor.ProcessBatchAsync(image, windowHandle, cancellationToken)
                .ConfigureAwait(false);
                
            try
            {
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 📦 バッチOCR処理完了（直接書き込み）{Environment.NewLine}");
            }
            catch { }
            
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
                var overlayPos = chunk.GetOverlayPosition();
                var overlaySize = chunk.GetOverlaySize();
                DebugLogUtility.WriteLog($"   インプレース位置: ({overlayPos.X},{overlayPos.Y}) [元座標と同じ]");
                DebugLogUtility.WriteLog($"   インプレースサイズ: ({overlaySize.Width},{overlaySize.Height}) [元サイズと同じ]");
                DebugLogUtility.WriteLog($"   計算フォントサイズ: {chunk.CalculateOptimalFontSize()}px (Height {chunk.CombinedBounds.Height} * 0.45)");
                DebugLogUtility.WriteLog($"   インプレース表示可能: {chunk.CanShowInPlace()}");
                
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

            // インプレースオーバーレイ表示を優先的に使用
            var inPlaceOverlayManager = _serviceProvider.GetService<IInPlaceTranslationOverlayManager>();
            if (inPlaceOverlayManager != null)
            {
                _logger?.LogInformation("🎯 インプレースオーバーレイ表示開始 - チャンク数: {Count}", textChunks.Count);
                DebugLogUtility.WriteLog($"🎯 インプレースオーバーレイ表示開始 - チャンク数: {textChunks.Count}");
                
                try
                {
                    // インプレース翻訳オーバーレイマネージャーを初期化
                    await inPlaceOverlayManager.InitializeAsync().ConfigureAwait(false);
                    
                    // 各テキストチャンクをインプレースで表示
                    DebugLogUtility.WriteLog($"\n🎭 インプレース表示開始処理:");
                    foreach (var chunk in textChunks)
                    {
                        DebugLogUtility.WriteLog($"\n🔸 チャンク {chunk.ChunkId} インプレース表示判定:");
                        DebugLogUtility.WriteLog($"   インプレース表示可能: {chunk.CanShowInPlace()}");
                        DebugLogUtility.WriteLog($"   元座標: ({chunk.CombinedBounds.X},{chunk.CombinedBounds.Y})");
                        DebugLogUtility.WriteLog($"   元サイズ: ({chunk.CombinedBounds.Width},{chunk.CombinedBounds.Height})");
                        
                        if (chunk.CanShowInPlace())
                        {
                            _logger?.LogDebug("🎭 インプレース表示 - ChunkId: {ChunkId}, 位置: ({X},{Y}), サイズ: ({W}x{H})", 
                                chunk.ChunkId, chunk.CombinedBounds.X, chunk.CombinedBounds.Y, 
                                chunk.CombinedBounds.Width, chunk.CombinedBounds.Height);
                            
                            await inPlaceOverlayManager!.ShowInPlaceOverlayAsync(chunk, cancellationToken)
                                .ConfigureAwait(false);
                            
                            DebugLogUtility.WriteLog($"   ✅ インプレース表示完了 - チャンク {chunk.ChunkId}");
                        }
                        else
                        {
                            _logger?.LogWarning("⚠️ インプレース表示条件を満たしていません - {InPlaceLog}", chunk.ToInPlaceLogString());
                            DebugLogUtility.WriteLog($"   ❌ インプレース表示スキップ - チャンク {chunk.ChunkId}: 条件未満足");
                        }
                    }
                    
                    _logger?.LogInformation("✅ インプレースオーバーレイ表示完了 - アクティブオーバーレイ数: {Count}", 
                        inPlaceOverlayManager!.ActiveOverlayCount);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "❌ インプレースオーバーレイ表示でエラーが発生");
                    DebugLogUtility.WriteLog($"❌❌❌ インプレースオーバーレイエラー: {ex.GetType().Name} - {ex.Message}");
                    
                    // インプレースUIでエラーが発生した場合は従来のオーバーレイにフォールバック
                    _logger?.LogWarning("🔄 従来のオーバーレイ表示にフォールバック");
                    await DisplayInPlaceTranslationOverlay(textChunks, cancellationToken).ConfigureAwait(false);
                }
            }
            else
            {
                // インプレースオーバーレイが利用できない場合は従来のオーバーレイを使用
                _logger?.LogWarning("⚠️ インプレースオーバーレイが利用できません。従来のオーバーレイを使用");
                await DisplayInPlaceTranslationOverlay(textChunks, cancellationToken).ConfigureAwait(false);
            }
            
            _logger?.LogInformation("🎉 座標ベース翻訳処理完了 - 座標ベース翻訳表示成功");
            DebugLogUtility.WriteLog("🎉 座標ベース翻訳処理完了 - 座標ベース翻訳表示成功");
            
            try
            {
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🎉 座標ベース翻訳処理完了（直接書き込み）{Environment.NewLine}");
            }
            catch { }
        }
        catch (TaskCanceledException)
        {
            // キャンセレーション例外は正常な処理として扱う
            try
            {
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔄 [DIRECT] 座標ベース翻訳処理がキャンセル: 正常終了{Environment.NewLine}");
            }
            catch (Exception fileEx)
            {
                System.Diagnostics.Debug.WriteLine($"CoordinateBasedTranslationService キャンセルログ書き込みエラー: {fileEx.Message}");
            }
            
            _logger?.LogDebug("座標ベース翻訳処理がキャンセルされました");
            return;
        }
        catch (Exception ex)
        {
            try
            {
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ❌ 座標ベース翻訳処理エラー（直接書き込み）: {ex.GetType().Name} - {ex.Message}{Environment.NewLine}");
            }
            catch { }
            
            _logger?.LogError(ex, "❌ 座標ベース翻訳処理でエラーが発生しました");
            throw;
        }
    }

    /// <summary>
    /// インプレース翻訳オーバーレイ表示
    /// </summary>
    private async Task DisplayInPlaceTranslationOverlay(
        IReadOnlyList<TextChunk> textChunks, 
        CancellationToken cancellationToken)
    {
        try
        {
            _logger?.LogDebug("🖼️ インプレース翻訳オーバーレイ表示開始");
            DebugLogUtility.WriteLog("🖼️ インプレース翻訳オーバーレイ表示開始");
            
            DebugLogUtility.WriteLog($"🔥🔥🔥 インプレース翻訳オーバーレイ表示直前 - _overlayManager null?: {_overlayManager == null}");
            if (_overlayManager != null)
            {
                // 各TextChunkを個別にインプレース表示
                foreach (var textChunk in textChunks)
                {
                    await _overlayManager.ShowInPlaceOverlayAsync(textChunk, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            DebugLogUtility.WriteLog("🔥🔥🔥 インプレース翻訳オーバーレイ表示完了");
        }
        catch (TaskCanceledException)
        {
            // キャンセレーション例外は正常な処理として扱う
            try
            {
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔄 [DIRECT] インプレース翻訳オーバーレイ表示がキャンセル: 正常終了{Environment.NewLine}");
            }
            catch (Exception fileEx)
            {
                System.Diagnostics.Debug.WriteLine($"CoordinateBasedTranslationService オーバーレイキャンセルログ書き込みエラー: {fileEx.Message}");
            }
            
            _logger?.LogDebug("インプレース翻訳オーバーレイ表示がキャンセルされました");
            return;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "❌ インプレース翻訳オーバーレイ表示でエラーが発生");
            DebugLogUtility.WriteLog($"❌❌❌ インプレース翻訳オーバーレイエラー: {ex.GetType().Name} - {ex.Message}");
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
            _logger?.LogInformation("🧹 CoordinateBasedTranslationService disposed - Hash: {Hash}", this.GetHashCode());
            
            // 直接書き込みで破棄ログ
            try
            {
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🧹 [DEBUG] CoordinateBasedTranslationService破棄: Hash={this.GetHashCode()}{Environment.NewLine}");
            }
            catch { }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "❌ CoordinateBasedTranslationService dispose error");
        }
    }
}