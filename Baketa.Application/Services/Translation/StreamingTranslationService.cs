using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Translation.Models;
using Microsoft.Extensions.Logging;

namespace Baketa.Application.Services.Translation;

/// <summary>
/// ストリーミング翻訳サービス実装
/// 🔥 [STREAMING] 段階的結果表示により12.7秒待機→数秒で表示開始を実現
/// </summary>
public class StreamingTranslationService : IStreamingTranslationService
{
    private readonly ITranslationService _translationService;
    private readonly ILogger<StreamingTranslationService> _logger;
    private readonly Core.Translation.Models.TranslationProgress _progress;
    private readonly object _progressLock = new();
    
    // チャンクサイズ設定（パフォーマンス最適化）
    private const int OptimalChunkSize = 3; // 3つずつ処理して段階的表示
    private const int MaxParallelChunks = 2; // 並列処理数
    
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
        System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚨 [CRITICAL_DEBUG] TranslateBatchWithStreamingAsync開始 - テキスト数: {texts?.Count ?? 0}{Environment.NewLine}");
            
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
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                
                // チャンクの全テキストを一度にバッチ翻訳（並列チャンク処理を活用）
                var chunkTexts = chunk.Texts;
                
                Console.WriteLine($"🔥 [STREAMING+PARALLEL] チャンク内バッチ翻訳開始 - テキスト数: {chunkTexts.Count}");
                var batchResults = await _translationService.TranslateBatchAsync(
                    chunkTexts,
                    sourceLanguage,
                    targetLanguage,
                    null,
                    combinedCts.Token).ConfigureAwait(false);
                
                // バッチ翻訳結果をチャンクの対応位置に配置
                for (int j = 0; j < chunkTexts.Count && j < batchResults.Count; j++)
                {
                    var translatedText = batchResults[j].TranslatedText;
                    results[chunk.StartIndex + j] = translatedText ?? chunkTexts[j];
                    
                    // チャンク内の各完了をコールバック通知
                    Console.WriteLine($"📢 [STREAMING+PARALLEL] チャンク完了通知 - インデックス: {chunk.StartIndex + j}");
                    onChunkCompleted?.Invoke(chunk.StartIndex + j, translatedText ?? chunkTexts[j]);
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
                _logger.LogWarning(ex, "🔥 [STREAMING+PARALLEL] チャンクバッチ翻訳エラー - チャンク: {Start}-{End}", 
                    chunk.StartIndex, chunk.EndIndex);
                    
                // エラー時はプレースホルダーを設定
                for (int j = 0; j < chunk.Texts.Count; j++)
                {
                    results[chunk.StartIndex + j] = $"[翻訳エラー] {chunk.Texts[j]}";
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
    
    private class ChunkInfo
    {
        public int StartIndex { get; set; }
        public int EndIndex { get; set; }
        public List<string> Texts { get; set; } = new();
    }
}