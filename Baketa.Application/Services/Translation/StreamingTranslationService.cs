using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Translation.Models;
using Microsoft.Extensions.Logging;

namespace Baketa.Application.Services.Translation;

/// <summary>
/// ストリーミング翻訳サービス実装
/// 🔥 [STREAMING] 段階的結果表示により12.7秒待機→数秒で表示開始を実現
/// 🎯 Phase 2タスク3: エラーハンドリング統一 - フォールバック戦略付き翻訳
/// </summary>
public class StreamingTranslationService : IStreamingTranslationService
{
    private readonly ITranslationService _translationService;
    // 🚨 [REGRESSION_FIX] エラーハンドリング統一による回帰問題を修正するため一時的に無効化
    // private readonly ITranslationErrorHandlerService _errorHandlerService;
    private readonly ILogger<StreamingTranslationService> _logger;
    private readonly Core.Translation.Models.TranslationProgress _progress;
    private readonly object _progressLock = new();
    
    // チャンクサイズ設定（パフォーマンス最適化）
    private const int OptimalChunkSize = 3; // 3つずつ処理して段階的表示
    private const int MaxParallelChunks = 2; // 並列処理数
    
    // 🚀 [DYNAMIC_TIMEOUT] 動的タイムアウト設定定数
    private const int BaseTimeoutSeconds = 30; // 基本タイムアウト（秒）
    private const int TimeoutExtensionThreshold = 500; // タイムアウト延長を開始する文字数
    private const double TimeoutExtensionPercentage = 0.5; // 500文字ごとに50%延長
    private const int MaxTimeoutMultiplier = 10; // 最大10倍まで延長
    
    public StreamingTranslationService(
        ITranslationService translationService,
        ILogger<StreamingTranslationService> logger)
    {
        _translationService = translationService ?? throw new ArgumentNullException(nameof(translationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _progress = new Core.Translation.Models.TranslationProgress();
        
        Console.WriteLine("🔥 [STREAMING] StreamingTranslationService初期化完了");
        _logger.LogInformation("StreamingTranslationService初期化完了");
    }
    
    /// <inheritdoc/>
    public async Task<List<string>> TranslateBatchWithStreamingAsync(
        IList<string> texts,
        Language sourceLanguage,
        Language targetLanguage,
        Action<int, string> onChunkCompleted,
        CancellationToken cancellationToken = default)
    {
        // 🚨 [CRITICAL_DEBUG] メソッド開始の即座ログ出力
        Console.WriteLine($"🚨 [CRITICAL_DEBUG] TranslateBatchWithStreamingAsync開始 - テキスト数: {texts?.Count ?? 0}");
        Console.WriteLine($"🔍 [LANGUAGE_DEBUG] 受信した言語設定: Source={sourceLanguage?.Code}({sourceLanguage?.DisplayName}) → Target={targetLanguage?.Code}({targetLanguage?.DisplayName})");
        System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚨 [CRITICAL_DEBUG] TranslateBatchWithStreamingAsync開始 - テキスト数: {texts?.Count ?? 0}{Environment.NewLine}");
        System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔍 [LANGUAGE_DEBUG] 受信した言語設定: Source={sourceLanguage?.Code}({sourceLanguage?.DisplayName}) → Target={targetLanguage?.Code}({targetLanguage?.DisplayName}){Environment.NewLine}");
            
        if (texts == null || texts.Count == 0)
        {
            var textsStatus = texts == null ? "null" : "empty";
            Console.WriteLine($"🚨 [CRITICAL_DEBUG] テキストリスト空のため早期リターン - texts={textsStatus}");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚨 [CRITICAL_DEBUG] テキストリスト空のため早期リターン - texts={textsStatus}{Environment.NewLine}");
            return new List<string>();
        }
        
        Console.WriteLine($"🚨 [CRITICAL_DEBUG] Stopwatch開始前");
        System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚨 [CRITICAL_DEBUG] Stopwatch開始前{Environment.NewLine}");
            
        var stopwatch = Stopwatch.StartNew();
        
        Console.WriteLine($"🚨 [CRITICAL_DEBUG] Logger情報出力前");
        System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚨 [CRITICAL_DEBUG] Logger情報出力前{Environment.NewLine}");
            
        _logger.LogInformation("🔥 [STREAMING] バッチ翻訳開始 - テキスト数: {Count}", texts.Count);
        Console.WriteLine($"🔥 [STREAMING] バッチ翻訳開始 - テキスト数: {texts.Count}");
        
        Console.WriteLine($"🚨 [CRITICAL_DEBUG] 進行状況初期化前");
        System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚨 [CRITICAL_DEBUG] 進行状況初期化前{Environment.NewLine}");
        
        // 進行状況初期化
        lock (_progressLock)
        {
            Console.WriteLine($"🚨 [CRITICAL_DEBUG] lockブロック内部に到達");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚨 [CRITICAL_DEBUG] lockブロック内部に到達{Environment.NewLine}");
                
            _progress.TotalChunks = texts.Count;
            _progress.CompletedChunks = 0;
            _progress.CurrentChunkIndex = 0;
            
            Console.WriteLine($"🚨 [CRITICAL_DEBUG] 進行状況初期化完了");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚨 [CRITICAL_DEBUG] 進行状況初期化完了{Environment.NewLine}");
        }
        
        var results = new string[texts.Count];
        var chunks = CreateChunks(texts, OptimalChunkSize);
        
        Console.WriteLine($"📦 [STREAMING] {chunks.Count}個のチャンクに分割（各{OptimalChunkSize}アイテム）");
        
        // チャンクごとに並列処理
        var semaphore = new SemaphoreSlim(MaxParallelChunks, MaxParallelChunks);
        var tasks = new List<Task>();
        
        foreach (var chunk in chunks)
        {
            var chunkTask = ProcessChunkAsync(
                chunk,
                sourceLanguage,
                targetLanguage,
                results,
                onChunkCompleted,
                semaphore,
                stopwatch,
                cancellationToken);
            
            tasks.Add(chunkTask);
        }
        
        // すべてのチャンクの完了を待つ
        await Task.WhenAll(tasks).ConfigureAwait(false);
        
        stopwatch.Stop();
        _logger.LogInformation("✅ [STREAMING] バッチ翻訳完了 - 総時間: {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
        Console.WriteLine($"✅ [STREAMING] バッチ翻訳完了 - 総時間: {stopwatch.ElapsedMilliseconds}ms");
        
        return results.ToList();
    }
    
    /// <inheritdoc/>
    public Core.Translation.Models.TranslationProgress GetProgress()
    {
        lock (_progressLock)
        {
            return new Core.Translation.Models.TranslationProgress
            {
                TotalChunks = _progress.TotalChunks,
                CompletedChunks = _progress.CompletedChunks,
                CurrentChunkIndex = _progress.CurrentChunkIndex,
                EstimatedRemainingMs = _progress.EstimatedRemainingMs
            };
        }
    }
    
    private async Task ProcessChunkAsync(
        ChunkInfo chunk,
        Language sourceLanguage,
        Language targetLanguage,
        string[] results,
        Action<int, string> onChunkCompleted,
        SemaphoreSlim semaphore,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        
        try
        {
            var chunkStopwatch = Stopwatch.StartNew();
            Console.WriteLine($"🚀 [STREAMING] チャンク処理開始 - インデックス: {chunk.StartIndex}-{chunk.EndIndex}");
            
            // 🔥 [STREAMING + PARALLEL] チャンク全体を一度にバッチ翻訳で処理
            if (cancellationToken.IsCancellationRequested)
                return;
                
            try
            {
                // 🚀 [DYNAMIC_TIMEOUT] テキスト量に応じた動的タイムアウト実装（Geminiレビュー対応）
                var chunkTexts = chunk.Texts;
                var totalTextLength = chunkTexts.Sum(t => t.Length);
                
                // 期待する計算: 基本30秒 + 500文字を超える部分について500文字ごとに15秒（50%）を加算
                var timeoutSeconds = BaseTimeoutSeconds;
                if (totalTextLength > TimeoutExtensionThreshold)
                {
                    var excessCharacters = totalTextLength - TimeoutExtensionThreshold;
                    var extensionChunks = Math.Ceiling((double)excessCharacters / TimeoutExtensionThreshold); // 浮動小数点計算
                    var maxExtensionChunks = Math.Min(extensionChunks, MaxTimeoutMultiplier - 1); // 最大9回延長（10倍まで）
                    
                    timeoutSeconds += (int)(BaseTimeoutSeconds * TimeoutExtensionPercentage * maxExtensionChunks);
                }
                
                Console.WriteLine($"⏰ [STREAMING+TIMEOUT] 動的タイムアウト設定 - チャンク文字数: {totalTextLength}, タイムアウト: {timeoutSeconds}秒");
                
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                
                // 🎯 Phase 2タスク3: エラーハンドリング統一 - 個別テキストのフォールバック処理
                Console.WriteLine($"🔥 [STREAMING+ERROR_HANDLER] チャンク内フォールバック翻訳開始 - テキスト数: {chunkTexts.Count}");
                
                // 各テキストを個別にフォールバック戦略付きで翻訳
                var translationTasks = new List<Task<(int index, string result)>>();
                
                for (int j = 0; j < chunkTexts.Count; j++)
                {
                    var textIndex = chunk.StartIndex + j;
                    var text = chunkTexts[j];
                    
                    Console.WriteLine($"🔍 [TRANSLATE_DEBUG] TranslateTextWithFallbackAsync呼び出し - Index: {textIndex}, Text: '{text}', Lang: {sourceLanguage.Code} → {targetLanguage.Code}");
                    
                    var task = TranslateTextWithFallbackAsync(
                        textIndex, 
                        text, 
                        sourceLanguage.Code, 
                        targetLanguage.Code, 
                        combinedCts.Token);
                    
                    translationTasks.Add(task);
                }
                
                // すべてのテキストの翻訳完了を待機
                var translatedResults = await Task.WhenAll(translationTasks).ConfigureAwait(false);
                
                // 結果を配置し、コールバック通知
                foreach (var (index, result) in translatedResults)
                {
                    results[index] = result;
                    Console.WriteLine($"📢 [STREAMING+ERROR_HANDLER] フォールバック翻訳完了通知 - インデックス: {index}");
                    onChunkCompleted?.Invoke(index, result);
                }
                
                // チャンク全体の進行状況更新
                lock (_progressLock)
                {
                    _progress.CompletedChunks += chunkTexts.Count;
                    _progress.CurrentChunkIndex = chunk.EndIndex;
                    
                    // 推定残り時間計算
                    if (_progress.CompletedChunks > 0)
                    {
                        var avgTimePerChunk = stopwatch.ElapsedMilliseconds / _progress.CompletedChunks;
                        var remainingChunks = _progress.TotalChunks - _progress.CompletedChunks;
                        _progress.EstimatedRemainingMs = avgTimePerChunk * remainingChunks;
                    }
                }
                
                var currentProgress = GetProgress();
                Console.WriteLine($"✨ [STREAMING+PARALLEL] チャンク完了 [{chunk.StartIndex}-{chunk.EndIndex}] - " +
                                $"進行率: {currentProgress.ProgressPercentage:F1}% - " +
                                $"テキスト数: {chunkTexts.Count}");
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogWarning("🔥 [STREAMING+PARALLEL] チャンクバッチ翻訳タイムアウト/キャンセル - チャンク: {Start}-{End}, エラー: {Error}", 
                    chunk.StartIndex, chunk.EndIndex, ex.Message);
                    
                // エラー時はプレースホルダーを設定
                for (int j = 0; j < chunk.Texts.Count; j++)
                {
                    results[chunk.StartIndex + j] = $"[翻訳タイムアウト] {chunk.Texts[j]}";
                    onChunkCompleted?.Invoke(chunk.StartIndex + j, results[chunk.StartIndex + j]);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "🔥 [STREAMING+ERROR_HANDLER] チャンクフォールバック翻訳エラー - チャンク: {Start}-{End}", 
                    chunk.StartIndex, chunk.EndIndex);
                    
                // エラー時はプレースホルダーを設定
                for (int j = 0; j < chunk.Texts.Count; j++)
                {
                    results[chunk.StartIndex + j] = $"[フォールバック翻訳エラー] {chunk.Texts[j]}";
                    onChunkCompleted?.Invoke(chunk.StartIndex + j, results[chunk.StartIndex + j]);
                }
            }
            
            chunkStopwatch.Stop();
            Console.WriteLine($"⏱️ [STREAMING] チャンク処理完了 - 処理時間: {chunkStopwatch.ElapsedMilliseconds}ms");
        }
        finally
        {
            semaphore.Release();
        }
    }
    
    private List<ChunkInfo> CreateChunks(IList<string> texts, int chunkSize)
    {
        var chunks = new List<ChunkInfo>();
        
        for (int i = 0; i < texts.Count; i += chunkSize)
        {
            var chunkTexts = texts.Skip(i).Take(chunkSize).ToList();
            chunks.Add(new ChunkInfo
            {
                StartIndex = i,
                EndIndex = Math.Min(i + chunkSize - 1, texts.Count - 1),
                Texts = chunkTexts
            });
        }
        
        return chunks;
    }
    
    /// <summary>
    /// 🚨 [REGRESSION_FIX] 個別テキストの直接翻訳（エラーハンドリング統一無効化）
    /// </summary>
    private async Task<(int index, string result)> TranslateTextWithFallbackAsync(
        int index,
        string text,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        try
        {
            // 直接ITranslationServiceを使用してシンプルに翻訳
            var result = await _translationService.TranslateAsync(
                text,
                new Language { Code = sourceLanguage, DisplayName = sourceLanguage },
                new Language { Code = targetLanguage, DisplayName = targetLanguage },
                null,
                cancellationToken).ConfigureAwait(false);
            
            var translatedText = result?.TranslatedText ?? text;
            
            // 🔍 [TRANSLATION_DEBUG] 翻訳結果の詳細ログ出力
            Console.WriteLine($"🔍 [TRANSLATION_DEBUG] 翻訳結果 - Index: {index}, Source: '{text}', Result: '{translatedText}', Success: {result?.IsSuccess}");
            
            return (index, translatedText);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("🔄 翻訳キャンセル - インデックス: {Index}", index);
            return (index, $"[翻訳キャンセル] {text}");
        }
        catch (Exception ex)
        {
            // 🚨 [CRITICAL_FIX] エラー時は原文ではなく適切なエラーメッセージを返す
            _logger.LogError(ex, "💥 翻訳エラー - インデックス: {Index}, テキスト: '{Text}'", index, text);
            Console.WriteLine($"💥 [TRANSLATION_ERROR] 翻訳エラー詳細 - Index: {index}, Text: '{text}', Error: {ex.GetType().Name} - {ex.Message}");
            
            // エラーの種類に応じて適切なメッセージを返す
            string errorMessage = ex switch
            {
                TimeoutException => "[翻訳タイムアウト]",
                OperationCanceledException => "[翻訳キャンセル]", 
                HttpRequestException => "[通信エラー]",
                _ => "[翻訳エラー]"
            };
            
            return (index, errorMessage);
        }
    }
    
    private class ChunkInfo
    {
        public int StartIndex { get; set; }
        public int EndIndex { get; set; }
        public List<string> Texts { get; set; } = new();
    }
}