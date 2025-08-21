using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.OCR.Results;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.OCR.PostProcessing;

/// <summary>
/// 信頼度スコアベース再処理システム
/// OCR精度向上ロードマップ Phase 1 - 高優先度実装
/// </summary>
public sealed class ConfidenceBasedReprocessor(
    IOcrEngine ocrEngine,
    ILogger<ConfidenceBasedReprocessor> logger,
    ConfidenceReprocessingSettings? settings = null)
{
    private readonly IOcrEngine _ocrEngine = ocrEngine ?? throw new ArgumentNullException(nameof(ocrEngine));
    private readonly ILogger<ConfidenceBasedReprocessor> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly ConfidenceReprocessingSettings _settings = settings ?? ConfidenceReprocessingSettings.Default;

    /// <summary>
    /// 信頼度が低いTextChunkを特定し、必要に応じて再処理する
    /// </summary>
    /// <param name="textChunks">元のTextChunkリスト</param>
    /// <param name="originalImage">元の画像</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>再処理後のTextChunkリスト</returns>
    public async Task<IReadOnlyList<TextChunk>> ReprocessLowConfidenceChunksAsync(
        IReadOnlyList<TextChunk> textChunks,
        IImage originalImage,
        CancellationToken cancellationToken = default)
    {
        if (textChunks == null || textChunks.Count == 0)
            return textChunks ?? [];

        _logger.LogInformation("信頼度ベース再処理開始: {ChunkCount}個のチャンクを分析", textChunks.Count);
        
        // 直接ファイル書き込みで信頼度ベース再処理開始を記録
        try
        {
            // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
            
            // 設定情報をログ出力
            // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化{Environment.NewLine}");
            
            // 各チャンクの信頼度を詳細ログ出力
            for (int i = 0; i < textChunks.Count; i++)
            {
                var chunk = textChunks[i];
                var avgConfidence = chunk.TextResults.Count > 0 ? chunk.TextResults.Average(tr => tr.Confidence) : 0.0f;
                var minThreshold = _settings?.ReprocessingThreshold ?? 0.5f;
                // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化}{Environment.NewLine}");
            }
        }
        catch (Exception fileEx)
        {
            System.Diagnostics.Debug.WriteLine($"ConfidenceBasedReprocessor ファイル書き込みエラー: {fileEx.Message}");
        }

        var reprocessedChunks = new List<TextChunk>();
        var reprocessingTasks = new List<Task<TextChunk>>();

        try
        {
            // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
        }
        catch (Exception fileEx)
        {
            System.Diagnostics.Debug.WriteLine($"ConfidenceBasedReprocessor ループ開始ログ書き込みエラー: {fileEx.Message}");
        }

        foreach (var chunk in textChunks)
        {
            try
            {
                var averageConfidence = chunk.AverageConfidence;
                
                _logger.LogDebug("チャンク#{ChunkId} 信頼度分析: {Confidence:F3} (閾値: {Threshold:F3})", 
                    chunk.ChunkId, averageConfidence, _settings?.ReprocessingThreshold ?? 0.7);

                // ShouldReprocessの詳細ログ
                try
                {
                    // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
                }
                catch (Exception fileEx)
                {
                    System.Diagnostics.Debug.WriteLine($"ConfidenceBasedReprocessor ShouldReprocess判定ログ書き込みエラー: {fileEx.Message}");
                }

                var shouldReprocess = ShouldReprocess(chunk, averageConfidence);
                
                try
                {
                    // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
                }
                catch (Exception fileEx)
                {
                    System.Diagnostics.Debug.WriteLine($"ConfidenceBasedReprocessor ShouldReprocess結果ログ書き込みエラー: {fileEx.Message}");
                }

                if (shouldReprocess)
                {
                    _logger.LogInformation("低信頼度チャンク#{ChunkId}を再処理: 信頼度={Confidence:F3}, テキスト='{Text}'", 
                        chunk.ChunkId, averageConfidence, chunk.CombinedText);
                    
                    // 直接ファイル書き込みで低信頼度チャンク再処理を記録
                    try
                    {
                        // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
                    }
                    catch (Exception fileEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"ConfidenceBasedReprocessor 再処理ログ書き込みエラー: {fileEx.Message}");
                    }

                    // 非同期で再処理を実行
                    try
                    {
                        // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
                    }
                    catch (Exception fileEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"ConfidenceBasedReprocessor タスク作成ログ書き込みエラー: {fileEx.Message}");
                    }
                    
                    var reprocessingTask = ReprocessSingleChunkAsync(chunk, originalImage, cancellationToken);
                    reprocessingTasks.Add(reprocessingTask);
                    
                    try
                    {
                        // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
                    }
                    catch (Exception fileEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"ConfidenceBasedReprocessor タスク追加ログ書き込みエラー: {fileEx.Message}");
                    }
                }
                else
                {
                    reprocessedChunks.Add(chunk);
                    _logger.LogDebug("チャンク#{ChunkId}は再処理不要: 信頼度={Confidence:F3}", 
                        chunk.ChunkId, averageConfidence);
                }
            }
            catch (Exception ex)
            {
                try
                {
                    // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
                }
                catch (Exception fileEx)
                {
                    System.Diagnostics.Debug.WriteLine($"ConfidenceBasedReprocessor 例外ログ書き込みエラー: {fileEx.Message}");
                }
                
                _logger.LogError(ex, "チャンク#{ChunkId}の処理中にエラーが発生、元のチャンクを保持", chunk.ChunkId);
                reprocessedChunks.Add(chunk);
            }
        }
        
        try
        {
            // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
        }
        catch (Exception fileEx)
        {
            System.Diagnostics.Debug.WriteLine($"ConfidenceBasedReprocessor ループ完了ログ書き込みエラー: {fileEx.Message}");
        }

        // 再処理タスクの完了を待機
        try
        {
            // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
        }
        catch (Exception fileEx)
        {
            System.Diagnostics.Debug.WriteLine($"ConfidenceBasedReprocessor タスク数ログ書き込みエラー: {fileEx.Message}");
        }

        if (reprocessingTasks.Count > 0)
        {
            _logger.LogInformation("再処理実行中: {TaskCount}個のチャンクを並列処理", reprocessingTasks.Count);
            
            try
            {
                // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
            }
            catch (Exception fileEx)
            {
                System.Diagnostics.Debug.WriteLine($"ConfidenceBasedReprocessor Task.WhenAllログ書き込みエラー: {fileEx.Message}");
            }
            
            try
            {
                var reprocessedResults = await Task.WhenAll(reprocessingTasks).ConfigureAwait(false);
                
                try
                {
                    // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
                }
                catch (Exception fileEx)
                {
                    System.Diagnostics.Debug.WriteLine($"ConfidenceBasedReprocessor Task.WhenAll完了ログ書き込みエラー: {fileEx.Message}");
                }
                
                reprocessedChunks.AddRange(reprocessedResults);
            }
            catch (TaskCanceledException)
            {
                // キャンセレーション発生時は部分的な結果を取得
                try
                {
                    // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
                }
                catch (Exception fileEx)
                {
                    System.Diagnostics.Debug.WriteLine($"ConfidenceBasedReprocessor キャンセル検出ログ書き込みエラー: {fileEx.Message}");
                }
                
                // 完了したタスクの結果を収集
                foreach (var task in reprocessingTasks)
                {
                    if (task.IsCompletedSuccessfully && task.Result != null)
                    {
                        reprocessedChunks.Add(task.Result);
                    }
                }
                
                _logger.LogWarning("再処理タスクがキャンセルされました。完了した{CompletedCount}個のタスクの結果を使用します。", 
                    reprocessedChunks.Count);
            }

            var improvementCount = reprocessedChunks.Where(r => r != null).Count(r => r!.AverageConfidence > (_settings?.ReprocessingThreshold ?? 0.7));
            _logger.LogInformation("再処理完了: {TotalCount}個中{ImprovedCount}個が改善", 
                reprocessedChunks.Count, improvementCount);
        }
        else
        {
            try
            {
                // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
            }
            catch (Exception fileEx)
            {
                System.Diagnostics.Debug.WriteLine($"ConfidenceBasedReprocessor タスクスキップログ書き込みエラー: {fileEx.Message}");
            }
        }

        // ChunkIdでソートして順序を保持
        var finalResult = reprocessedChunks.OrderBy(c => c.ChunkId).ToList();
        
        _logger.LogInformation("信頼度ベース再処理完了: 最終チャンク数={FinalCount}", finalResult.Count);
        return finalResult.AsReadOnly();
    }

    /// <summary>
    /// 単一チャンクの再処理を実行
    /// </summary>
    private async Task<TextChunk> ReprocessSingleChunkAsync(
        TextChunk originalChunk,
        IImage originalImage,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("チャンク#{ChunkId}の個別再処理開始", originalChunk.ChunkId);

            // 直接ファイル書き込みで再処理開始を記録
            try
            {
                // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化{Environment.NewLine}");
            }
            catch (Exception fileEx)
            {
                System.Diagnostics.Debug.WriteLine($"ConfidenceBasedReprocessor 個別再処理ログ書き込みエラー: {fileEx.Message}");
            }

            // 1. 画像の有効性を事前確認
            if (!IsImageValid(originalImage))
            {
                DebugLogUtility.WriteLog($"画像有効性チェック失敗: チャンク#{originalChunk.ChunkId}の再処理をスキップ");
                return originalChunk;
            }
            
            // 2. 領域を少し拡張してOCRを再実行
            var expandedBounds = ExpandBoundsForReprocessing(originalChunk.CombinedBounds, originalImage);
            
            // 直接ファイル書き込みで拡張領域をログ出力
            try
            {
                // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化 → 拡張領域=({expandedBounds.X},{expandedBounds.Y},{expandedBounds.Width},{expandedBounds.Height}){Environment.NewLine}");
            }
            catch (Exception fileEx)
            {
                System.Diagnostics.Debug.WriteLine($"ConfidenceBasedReprocessor 拡張領域ログ書き込みエラー: {fileEx.Message}");
            }
            
            // 2. OCRエンジンの初期化状態を確認・保証
            await EnsureOcrEngineInitializedAsync(cancellationToken).ConfigureAwait(false);
            
            // 3. 改善された設定でOCRを再実行
            var enhancedSettings = CreateEnhancedOcrSettings();
            var originalSettings = _ocrEngine.GetSettings();
            
            // 設定変更をログ出力
            try
            {
                // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
            }
            catch (Exception fileEx)
            {
                System.Diagnostics.Debug.WriteLine($"ConfidenceBasedReprocessor 設定変更ログ書き込みエラー: {fileEx.Message}");
            }
            
            await _ocrEngine.ApplySettingsAsync(enhancedSettings, cancellationToken).ConfigureAwait(false);

            try
            {
                // 直接ファイル書き込みでOCR再実行開始をログ出力
                try
                {
                    // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
                }
                catch (Exception fileEx)
                {
                    System.Diagnostics.Debug.WriteLine($"ConfidenceBasedReprocessor OCR再実行開始ログ書き込みエラー: {fileEx.Message}");
                }

                // 3. 拡張された領域でOCRを再実行
                var reprocessedResults = await _ocrEngine.RecognizeAsync(originalImage, expandedBounds, progressCallback: null, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                // 直接ファイル書き込みでOCR再実行結果をログ出力
                try
                {
                    // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
                    
                    if (reprocessedResults.HasText && reprocessedResults.TextRegions.Count > 0)
                    {
                        foreach (var region in reprocessedResults.TextRegions.Take(3)) // 最初の3個のみログ出力
                        {
                            // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化{Environment.NewLine}");
                        }
                    }
                }
                catch (Exception fileEx)
                {
                    System.Diagnostics.Debug.WriteLine($"ConfidenceBasedReprocessor OCR再実行結果ログ書き込みエラー: {fileEx.Message}");
                }

                // 4. 再処理結果を評価（座標補正のため拡張領域情報を渡す）
                var improvedChunk = EvaluateReprocessingResults(originalChunk, reprocessedResults, expandedBounds);
                
                // 結果をログ出力
                try
                {
                    // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化{Environment.NewLine}");
                }
                catch (Exception fileEx)
                {
                    System.Diagnostics.Debug.WriteLine($"ConfidenceBasedReprocessor 完了ログ書き込みエラー: {fileEx.Message}");
                }
                
                _logger.LogDebug("チャンク#{ChunkId}再処理完了: 元信頼度={OriginalConf:F3} → 新信頼度={NewConf:F3}", 
                    originalChunk.ChunkId, originalChunk.AverageConfidence, improvedChunk.AverageConfidence);

                return improvedChunk;
            }
            catch (Exception ocrEx)
            {
                // OCR処理中の例外をログ出力
                try
                {
                    // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
                }
                catch (Exception fileEx)
                {
                    System.Diagnostics.Debug.WriteLine($"ConfidenceBasedReprocessor OCR例外ログ書き込みエラー: {fileEx.Message}");
                }
                
                _logger.LogWarning(ocrEx, "チャンク#{ChunkId}のOCR再実行でエラー発生", originalChunk.ChunkId);
                return originalChunk;
            }
            finally
            {
                // 設定を元に戻す
                try
                {
                    await _ocrEngine.ApplySettingsAsync(originalSettings, cancellationToken).ConfigureAwait(false);
                    
                    // 設定復元をログ出力
                    try
                    {
                        // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
                    }
                    catch (Exception fileEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"ConfidenceBasedReprocessor 設定復元ログ書き込みエラー: {fileEx.Message}");
                    }
                }
                catch (Exception settingsEx)
                {
                    _logger.LogWarning(settingsEx, "OCR設定の復元でエラー発生");
                }
            }
        }
        catch (TaskCanceledException)
        {
            // キャンセレーション例外は正常な処理として扱う
            try
            {
                // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
            }
            catch (Exception fileEx)
            {
                System.Diagnostics.Debug.WriteLine($"ConfidenceBasedReprocessor キャンセルログ書き込みエラー: {fileEx.Message}");
            }
            
            _logger.LogDebug("チャンク#{ChunkId}の再処理がキャンセルされました", originalChunk.ChunkId);
            return originalChunk;
        }
        catch (Exception ex)
        {
            // 直接ファイル書き込みで全体例外をログ出力
            try
            {
                // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
            }
            catch (Exception fileEx)
            {
                System.Diagnostics.Debug.WriteLine($"ConfidenceBasedReprocessor 全体例外ログ書き込みエラー: {fileEx.Message}");
            }
            
            _logger.LogWarning(ex, "チャンク#{ChunkId}の再処理でエラー発生、元のチャンクを保持", originalChunk.ChunkId);
            return originalChunk;
        }
    }

    /// <summary>
    /// チャンクを再処理すべきかどうかを判定
    /// </summary>
    private bool ShouldReprocess(TextChunk chunk, float averageConfidence)
    {
        try
        {
            // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
        }
        catch (Exception fileEx)
        {
            System.Diagnostics.Debug.WriteLine($"ConfidenceBasedReprocessor ShouldReprocess内部開始ログ書き込みエラー: {fileEx.Message}");
        }

        // 基本的な信頼度チェック
        if (averageConfidence >= _settings.ReprocessingThreshold)
        {
            try
            {
                // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
            }
            catch (Exception fileEx)
            {
                System.Diagnostics.Debug.WriteLine($"ConfidenceBasedReprocessor 再処理不要ログ書き込みエラー: {fileEx.Message}");
            }
            return false;
        }

        // 非常に短いテキストは再処理しない（ノイズの可能性）
        if (chunk.CombinedText.Length < _settings.MinimumTextLengthForReprocessing)
        {
            try
            {
                // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
            }
            catch (Exception fileEx)
            {
                System.Diagnostics.Debug.WriteLine($"ConfidenceBasedReprocessor テキスト長チェック不合格ログ書き込みエラー: {fileEx.Message}");
            }
            _logger.LogDebug("チャンク#{ChunkId}は短すぎるため再処理をスキップ: 長さ={Length}", 
                chunk.ChunkId, chunk.CombinedText.Length);
            return false;
        }

        // 極小領域は再処理しない
        if (chunk.CombinedBounds.Width < _settings.MinimumRegionSize.Width || 
            chunk.CombinedBounds.Height < _settings.MinimumRegionSize.Height)
        {
            try
            {
                // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化 < ({_settings.MinimumRegionSize.Width}x{_settings.MinimumRegionSize.Height}){Environment.NewLine}");
            }
            catch (Exception fileEx)
            {
                System.Diagnostics.Debug.WriteLine($"ConfidenceBasedReprocessor 領域サイズチェック不合格ログ書き込みエラー: {fileEx.Message}");
            }
            _logger.LogDebug("チャンク#{ChunkId}は小さすぎるため再処理をスキップ: サイズ=({Width}x{Height})", 
                chunk.ChunkId, chunk.CombinedBounds.Width, chunk.CombinedBounds.Height);
            return false;
        }

        // 特定のパターンをチェック（数字のみ、記号のみなど）
        var isNoise = IsLikelyNoise(chunk.CombinedText);
        if (isNoise)
        {
            try
            {
                // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
            }
            catch (Exception fileEx)
            {
                System.Diagnostics.Debug.WriteLine($"ConfidenceBasedReprocessor ノイズチェック不合格ログ書き込みエラー: {fileEx.Message}");
            }
            _logger.LogDebug("チャンク#{ChunkId}はノイズと判定、再処理をスキップ: テキスト='{Text}'", 
                chunk.ChunkId, chunk.CombinedText);
            return false;
        }

        try
        {
            // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
        }
        catch (Exception fileEx)
        {
            System.Diagnostics.Debug.WriteLine($"ConfidenceBasedReprocessor ShouldReprocess合格ログ書き込みエラー: {fileEx.Message}");
        }

        return true;
    }

    /// <summary>
    /// 画像の有効性をチェック
    /// </summary>
    private bool IsImageValid(IImage image)
    {
        if (image == null)
        {
            DebugLogUtility.WriteLog("IsImageValid: 画像がnull");
            return false;
        }
        
        try
        {
            // Widthプロパティにアクセスして有効性を確認
            var width = image.Width;
            var height = image.Height;
            
            if (width <= 0 || height <= 0)
            {
                DebugLogUtility.WriteLog($"IsImageValid: 無効な画像サイズ {width}x{height}");
                return false;
            }
            
            DebugLogUtility.WriteLog($"IsImageValid: 画像有効 {width}x{height}");
            return true;
        }
        catch (ObjectDisposedException ex)
        {
            DebugLogUtility.WriteLog($"IsImageValid: 画像が破棄済み {ex.Message}");
            return false;
        }
        catch (InvalidOperationException ex)
        {
            DebugLogUtility.WriteLog($"IsImageValid: 画像が無効状態 {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            DebugLogUtility.WriteLog($"IsImageValid: 未知のエラー {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 再処理用に領域を拡張
    /// </summary>
    private System.Drawing.Rectangle ExpandBoundsForReprocessing(
        System.Drawing.Rectangle originalBounds, 
        IImage image)
    {
        // 事前に画像の有効性をチェック
        if (!IsImageValid(image))
        {
            _logger.LogWarning("ExpandBoundsForReprocessing: 画像が無効です");
            return originalBounds;
        }
        
        try
        {
            var expansion = _settings.BoundsExpansionPixels;
            
            var expandedX = Math.Max(0, originalBounds.X - expansion);
            var expandedY = Math.Max(0, originalBounds.Y - expansion);
            var expandedWidth = Math.Min(image.Width - expandedX, originalBounds.Width + expansion * 2);
            var expandedHeight = Math.Min(image.Height - expandedY, originalBounds.Height + expansion * 2);

            var expandedBounds = new System.Drawing.Rectangle(expandedX, expandedY, expandedWidth, expandedHeight);
            
            DebugLogUtility.WriteLog($"領域拡張: {originalBounds} → {expandedBounds} (画像: {image.Width}x{image.Height})");
            
            return expandedBounds;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "画像の領域拡張中にエラーが発生しました");
            DebugLogUtility.WriteLog($"領域拡張エラー: {ex.Message}");
            return originalBounds;
        }
    }

    /// <summary>
    /// 画像領域を抽出
    /// </summary>
    private IAdvancedImage ExtractImageRegion(IAdvancedImage originalImage, System.Drawing.Rectangle _)
    {
        // TODO: 実際の画像切り出し実装
        // 現在は元画像を返す（実装簡略化）
        return originalImage;
    }

    /// <summary>
    /// 【Phase 2強化】再処理用の強化されたOCR設定を作成
    /// 日本語文字検出に特化した最適化設定
    /// </summary>
    private OcrEngineSettings CreateEnhancedOcrSettings()
    {
        var currentSettings = _ocrEngine.GetSettings();
        var enhancedSettings = currentSettings.Clone();

        // 【Phase 2改善】日本語文字検出に特化した設定調整
        
        // 1. 検出閾値の最適化 - より低い閾値で微細な文字も捕捉
        enhancedSettings.DetectionThreshold = Math.Max(0.03, currentSettings.DetectionThreshold * 0.5);
        
        // 2. 認識閾値の調整 - 中国語文字も含めて幅広く認識
        enhancedSettings.RecognitionThreshold = Math.Max(0.1, currentSettings.RecognitionThreshold * 0.6);
        
        // 3. 前処理とLanguageModel強制有効化
        enhancedSettings.EnablePreprocessing = true;
        enhancedSettings.UseLanguageModel = true;
        
        // 4. 言語設定の最適化 - 日本語に特化
        enhancedSettings.Language = "jpn";
        
        // 5. 最大検出数の増加 - 細かい文字も見逃さない
        enhancedSettings.MaxDetections = Math.Max(currentSettings.MaxDetections, 300);
        
        // 6. 方向分類の有効化 - 回転したテキストにも対応
        enhancedSettings.UseDirectionClassification = true;
        
        // 7. マルチスレッド処理で高速化
        enhancedSettings.EnableMultiThread = true;
        enhancedSettings.WorkerCount = Math.Max(2, currentSettings.WorkerCount);

        // 【Phase 2ログ強化】設定変更の詳細ログ
        _logger.LogDebug("【Phase 2】再処理用設定作成: DetectionThreshold={DetectionThreshold:F3}, RecognitionThreshold={RecognitionThreshold:F3}, 前処理={Preprocessing}, LM={LanguageModel}, 最大検出数={MaxDetections}, 方向分類={DirectionClassification}", 
            enhancedSettings.DetectionThreshold, enhancedSettings.RecognitionThreshold, enhancedSettings.EnablePreprocessing, enhancedSettings.UseLanguageModel, enhancedSettings.MaxDetections, enhancedSettings.UseDirectionClassification);

        try
        {
            // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
            // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
        }
        catch (Exception fileEx)
        {
            System.Diagnostics.Debug.WriteLine($"ConfidenceBasedReprocessor Phase 2設定ログ書き込みエラー: {fileEx.Message}");
        }

        return enhancedSettings;
    }

    /// <summary>
    /// 再処理結果を評価し、改善されたチャンクを作成
    /// </summary>
    private TextChunk EvaluateReprocessingResults(
        TextChunk originalChunk,
        OcrResults reprocessedResults,
        System.Drawing.Rectangle expandedBounds)
    {
        if (!reprocessedResults.HasText || reprocessedResults.TextRegions.Count == 0)
        {
            _logger.LogDebug("再処理結果にテキストなし、元のチャンクを保持");
            return originalChunk;
        }

        // 再処理結果から最適な領域を選択
        var bestRegion = SelectBestRegionFromReprocessing(reprocessedResults.TextRegions);
        
        if (bestRegion == null)
        {
            _logger.LogDebug("再処理結果に適切な領域なし、元のチャンクを保持");
            return originalChunk;
        }

        // 改善されたかどうかを判定
        if (bestRegion.Confidence <= originalChunk.AverageConfidence + _settings.MinimumImprovementThreshold)
        {
            _logger.LogDebug("再処理結果が十分改善されていない: {Original:F3} → {New:F3}", 
                originalChunk.AverageConfidence, bestRegion.Confidence);
            return originalChunk;
        }

        // 座標を元の画像座標系に変換（拡張領域のオフセットを加算）
        var correctedBounds = new System.Drawing.Rectangle(
            bestRegion.Bounds.X + expandedBounds.X,
            bestRegion.Bounds.Y + expandedBounds.Y,
            bestRegion.Bounds.Width,
            bestRegion.Bounds.Height
        );

        // 改善されたチャンクを作成
        var improvedTextResult = new PositionedTextResult
        {
            Text = bestRegion.Text,
            BoundingBox = correctedBounds, // 座標修正済み
            Confidence = (float)bestRegion.Confidence,
            ChunkId = originalChunk.ChunkId,
            ProcessingTime = reprocessedResults.ProcessingTime,
            DetectedLanguage = reprocessedResults.LanguageCode
        };

        var improvedChunk = new TextChunk
        {
            ChunkId = originalChunk.ChunkId,
            TextResults = [improvedTextResult],
            CombinedBounds = correctedBounds, // 座標修正済み
            CombinedText = bestRegion.Text,
            SourceWindowHandle = originalChunk.SourceWindowHandle,
            DetectedLanguage = reprocessedResults.LanguageCode,
            TranslatedText = originalChunk.TranslatedText // 翻訳は保持
        };

        _logger.LogInformation("チャンク#{ChunkId}が改善: '{OriginalText}' (信頼度:{OriginalConf:F3}) → '{NewText}' (信頼度:{NewConf:F3})", 
            originalChunk.ChunkId, originalChunk.CombinedText, originalChunk.AverageConfidence, 
            improvedChunk.CombinedText, improvedChunk.AverageConfidence);

        return improvedChunk;
    }

    /// <summary>
    /// 再処理結果から最適な領域を選択
    /// </summary>
    private OcrTextRegion? SelectBestRegionFromReprocessing(
        IReadOnlyList<OcrTextRegion> regions)
    {
        if (regions.Count == 0)
            return null;

        // 信頼度が基準を満たす領域をフィルタリング
        var candidateRegions = regions.Where(r => 
            r.Confidence >= _settings.MinimumAcceptableConfidence &&
            !string.IsNullOrWhiteSpace(r.Text))
            .ToList();

        if (candidateRegions.Count == 0)
        {
            // 基準を満たさない場合は、最も信頼度の高い領域を返す
            return regions.Where(r => !string.IsNullOrWhiteSpace(r.Text))
                         .OrderByDescending(r => r.Confidence)
                         .FirstOrDefault();
        }

        // 信頼度が最も高い領域を選択
        return candidateRegions.OrderByDescending(r => r.Confidence).First();
    }

    /// <summary>
    /// 2つの矩形の重複率を計算
    /// </summary>
    private static double CalculateOverlapRatio(System.Drawing.Rectangle rect1, System.Drawing.Rectangle rect2)
    {
        var intersection = System.Drawing.Rectangle.Intersect(rect1, rect2);
        if (intersection.IsEmpty)
            return 0.0;

        var intersectionArea = intersection.Width * intersection.Height;
        var rect1Area = rect1.Width * rect1.Height;
        var rect2Area = rect2.Width * rect2.Height;
        var unionArea = rect1Area + rect2Area - intersectionArea;

        return unionArea > 0 ? (double)intersectionArea / unionArea : 0.0;
    }

    /// <summary>
    /// テキストがノイズかどうかを判定
    /// </summary>
    private static bool IsLikelyNoise(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return true;

        var trimmedText = text.Trim();
        
        // 単一文字で記号のみ
        if (trimmedText.Length == 1 && !char.IsLetterOrDigit(trimmedText[0]))
            return true;

        // 繰り返し文字（例: "..." や "---"）
        if (trimmedText.Length > 1 && trimmedText.All(c => c == trimmedText[0]))
            return true;

        // 非常に短く、意味のない組み合わせ
        if (trimmedText.Length <= 2 && trimmedText.All(c => ".,!?-_=+*#@()[]{}".Contains(c)))
            return true;

        return false;
    }

    /// <summary>
    /// 小さなテキストが含まれているかどうかを判定
    /// </summary>
    private static bool ContainsSmallText(TextChunk chunk)
    {
        return chunk.CombinedBounds.Height <= 20 || chunk.CombinedBounds.Width <= 50;
    }

    /// <summary>
    /// OCRエンジンが初期化されていることを保証する
    /// </summary>
    private async Task EnsureOcrEngineInitializedAsync(CancellationToken cancellationToken)
    {
        try
        {
            // OCRエンジンの初期化状態を確認（プロパティで確認）
            var isInitialized = _ocrEngine.IsInitialized;
            
            // 初期化ログを記録
            try
            {
                // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
            }
            catch (Exception fileEx)
            {
                System.Diagnostics.Debug.WriteLine($"ConfidenceBasedReprocessor 初期化状態ログ書き込みエラー: {fileEx.Message}");
            }
            
            if (!isInitialized)
            {
                // 初期化が必要な場合は実行
                try
                {
                    // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
                }
                catch (Exception fileEx)
                {
                    System.Diagnostics.Debug.WriteLine($"ConfidenceBasedReprocessor 初期化開始ログ書き込みエラー: {fileEx.Message}");
                }
                
                var initSuccess = await _ocrEngine.InitializeAsync(settings: null, cancellationToken: cancellationToken).ConfigureAwait(false);
                
                try
                {
                    // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
                }
                catch (Exception fileEx)
                {
                    System.Diagnostics.Debug.WriteLine($"ConfidenceBasedReprocessor 初期化完了ログ書き込みエラー: {fileEx.Message}");
                }
                
                if (initSuccess)
                {
                    _logger.LogInformation("ConfidenceBasedReprocessor: OCRエンジンを初期化しました");
                }
                else
                {
                    _logger.LogError("ConfidenceBasedReprocessor: OCRエンジンの初期化に失敗しました");
                    throw new InvalidOperationException("OCRエンジンの初期化に失敗しました");
                }
            }
        }
        catch (Exception ex)
        {
            // 初期化エラーをログ記録
            try
            {
                // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
            }
            catch (Exception fileEx)
            {
                System.Diagnostics.Debug.WriteLine($"ConfidenceBasedReprocessor 初期化エラーログ書き込みエラー: {fileEx.Message}");
            }
            
            _logger.LogError(ex, "ConfidenceBasedReprocessor: OCRエンジンの初期化に失敗しました");
            throw;
        }
    }
}

/// <summary>
/// 信頼度ベース再処理の設定
/// </summary>
public sealed class ConfidenceReprocessingSettings
{
    /// <summary>再処理を行う信頼度の閾値</summary>
    public float ReprocessingThreshold { get; init; } = 0.7f;

    /// <summary>再処理後の最小許容信頼度</summary>
    public double MinimumAcceptableConfidence { get; init; } = 0.5;

    /// <summary>改善とみなすための最小信頼度向上値</summary>
    public float MinimumImprovementThreshold { get; init; } = 0.1f;

    /// <summary>再処理対象とする最小テキスト長</summary>
    public int MinimumTextLengthForReprocessing { get; init; } = 1;

    /// <summary>再処理対象とする最小領域サイズ</summary>
    public System.Drawing.Size MinimumRegionSize { get; init; } = new(10, 10);

    /// <summary>領域拡張のピクセル数</summary>
    public int BoundsExpansionPixels { get; init; } = 5;

    /// <summary>領域の最小重複率</summary>
    public double MinimumOverlapRatio { get; init; } = 0.3;

    /// <summary>デフォルト設定</summary>
    public static ConfidenceReprocessingSettings Default => new();

    /// <summary>厳密な再処理設定（ゲーム向け）</summary>
    public static ConfidenceReprocessingSettings Strict => new()
    {
        ReprocessingThreshold = 0.8f,
        MinimumAcceptableConfidence = 0.6,
        MinimumImprovementThreshold = 0.15f,
        MinimumTextLengthForReprocessing = 3,
        BoundsExpansionPixels = 3
    };

    /// <summary>緩い再処理設定（小説向け）</summary>
    public static ConfidenceReprocessingSettings Relaxed => new()
    {
        ReprocessingThreshold = 0.6f,
        MinimumAcceptableConfidence = 0.4,
        MinimumImprovementThreshold = 0.05f,
        MinimumTextLengthForReprocessing = 1,
        BoundsExpansionPixels = 8,
        MinimumOverlapRatio = 0.2
    };
}
