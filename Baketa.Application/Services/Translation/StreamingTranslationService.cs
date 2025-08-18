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
    private const int BaseTimeoutSeconds = 120; // 🔧 [TIMEOUT_TEST] 基本タイムアウト（秒）- 30秒→120秒に延長してタイムアウト原因を確定検証
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
            return [];
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
        
        Console.WriteLine($"🚨 [CRITICAL_DEBUG] lockブロック脱出、CreateChunks呼び出し直前");
        System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚨 [CRITICAL_DEBUG] lockブロック脱出、CreateChunks呼び出し直前{Environment.NewLine}");
        
        Console.WriteLine($"🚨 [CRITICAL_DEBUG] results配列作成開始");
        System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚨 [CRITICAL_DEBUG] results配列作成開始{Environment.NewLine}");
        
        var results = new string[texts.Count];
        
        Console.WriteLine($"🚨 [CRITICAL_DEBUG] CreateChunks呼び出し開始 - OptimalChunkSize={OptimalChunkSize}");
        System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚨 [CRITICAL_DEBUG] CreateChunks呼び出し開始 - OptimalChunkSize={OptimalChunkSize}{Environment.NewLine}");
        
        var chunks = CreateChunks(texts, OptimalChunkSize);
        
        Console.WriteLine($"🚨 [CRITICAL_DEBUG] CreateChunks呼び出し完了");
        System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚨 [CRITICAL_DEBUG] CreateChunks呼び出し完了{Environment.NewLine}");
        
        Console.WriteLine($"🚨 [CRITICAL_DEBUG] CreateChunks完了 - チャンク数: {chunks?.Count ?? 0}");
        System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚨 [CRITICAL_DEBUG] CreateChunks完了 - チャンク数: {chunks?.Count ?? 0}{Environment.NewLine}");
        
        Console.WriteLine($"📦 [STREAMING] {chunks.Count}個のチャンクに分割（各{OptimalChunkSize}アイテム）");
        
        // 🚀 [STREAMING_FIX] 正常なチャンク処理によるストリーミング翻訳を実行
        Console.WriteLine($"🚀 [STREAMING_FIX] 通常のチャンク処理を実行 - 段階的結果表示");
        
        // 並列チャンク処理
        var semaphore = new SemaphoreSlim(MaxParallelChunks, MaxParallelChunks);
        
        Console.WriteLine($"🚨 [CHUNK_DEBUG] ProcessChunkAsync作成開始 - チャンク数: {chunks.Count}");
        var processingTasks = chunks.Select(chunk => 
            ProcessChunkAsync(chunk, sourceLanguage, targetLanguage, results, onChunkCompleted, semaphore, stopwatch, cancellationToken)
        ).ToArray(); // 🔧 [HANGUP_FIX] ToArray()で即座に評価、遅延実行を回避
        
        Console.WriteLine($"🚨 [CHUNK_DEBUG] ProcessChunkAsync配列作成完了 - タスク数: {processingTasks.Length}");
        
        // 🚀 [TRUE_BATCH_PROCESSING] バッチ翻訳により例外は各チャンク内で処理済み
        try
        {
            Console.WriteLine($"🚨 [CHUNK_DEBUG] Task.WhenAll実行開始");
            await Task.WhenAll(processingTasks).ConfigureAwait(false);
            Console.WriteLine($"✅ [TRUE_BATCH_PROCESSING] 全チャンク処理完了");
        }
        finally
        {
            semaphore?.Dispose();
        }
        
        stopwatch.Stop();
        _logger.LogInformation("✅ [STREAMING] バッチ翻訳完了 - 総時間: {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
        Console.WriteLine($"✅ [STREAMING] バッチ翻訳完了 - 総時間: {stopwatch.ElapsedMilliseconds}ms");
        
        return [.. results];
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
        Console.WriteLine($"🚨 [CHUNK_DEBUG] ProcessChunkAsync開始 - インデックス: {chunk.StartIndex}-{chunk.EndIndex}");
        System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚨 [CHUNK_DEBUG] ProcessChunkAsync開始 - インデックス: {chunk.StartIndex}-{chunk.EndIndex}{Environment.NewLine}");
        
        Console.WriteLine($"🚨 [CHUNK_DEBUG] semaphore.WaitAsync呼び出し前");
        System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚨 [CHUNK_DEBUG] semaphore.WaitAsync呼び出し前{Environment.NewLine}");
        
        // 🔧 [DEADLOCK_DEBUG] セマフォデッドロック調査のため詳細ログとタイムアウト追加
        using var semaphoreTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(60)); // 🔧 [EMERGENCY_FIX] セマフォ取得に60秒タイムアウト（Python翻訳サーバー重要処理対応）
        using var semaphoreCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, semaphoreTimeout.Token);
        
        try
        {
            Console.WriteLine($"🚨 [DEADLOCK_DEBUG] セマフォ取得試行開始 - インデックス: {chunk.StartIndex}-{chunk.EndIndex}, 利用可能数: {semaphore.CurrentCount}");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚨 [DEADLOCK_DEBUG] セマフォ取得試行開始 - インデックス: {chunk.StartIndex}-{chunk.EndIndex}, 利用可能数: {semaphore.CurrentCount}{Environment.NewLine}");
            
            await semaphore.WaitAsync(semaphoreCts.Token).ConfigureAwait(false);
            
            Console.WriteLine($"✅ [DEADLOCK_DEBUG] セマフォ取得成功 - インデックス: {chunk.StartIndex}-{chunk.EndIndex}, 残り利用可能数: {semaphore.CurrentCount}");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ✅ [DEADLOCK_DEBUG] セマフォ取得成功 - インデックス: {chunk.StartIndex}-{chunk.EndIndex}, 残り利用可能数: {semaphore.CurrentCount}{Environment.NewLine}");
        }
        catch (OperationCanceledException) when (semaphoreTimeout.Token.IsCancellationRequested)
        {
            Console.WriteLine($"⚠️ [DEADLOCK_DEBUG] セマフォ取得タイムアウト（60秒） - インデックス: {chunk.StartIndex}-{chunk.EndIndex}");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ⚠️ [DEADLOCK_DEBUG] セマフォ取得タイムアウト（60秒） - インデックス: {chunk.StartIndex}-{chunk.EndIndex}{Environment.NewLine}");
            
            // タイムアウト時はタイムアウトメッセージを返して処理を継続
            for (int j = 0; j < chunk.Texts.Count; j++)
            {
                results[chunk.StartIndex + j] = "[セマフォ取得タイムアウト]";
                onChunkCompleted?.Invoke(chunk.StartIndex + j, results[chunk.StartIndex + j]);
            }
            return; // early return でセマフォリリースをスキップ
        }
        
        Console.WriteLine($"🔧 [POST_SEMAPHORE] セマフォ取得後処理開始 - インデックス: {chunk.StartIndex}-{chunk.EndIndex}");
        System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔧 [POST_SEMAPHORE] セマフォ取得後処理開始 - インデックス: {chunk.StartIndex}-{chunk.EndIndex}{Environment.NewLine}");
        
        try
        {
            Console.WriteLine($"🔧 [TRY_BLOCK] try ブロック開始 - インデックス: {chunk.StartIndex}-{chunk.EndIndex}");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔧 [TRY_BLOCK] try ブロック開始 - インデックス: {chunk.StartIndex}-{chunk.EndIndex}{Environment.NewLine}");
            
            var chunkStopwatch = Stopwatch.StartNew();
            Console.WriteLine($"🔧 [STOPWATCH] Stopwatch.StartNew完了 - インデックス: {chunk.StartIndex}-{chunk.EndIndex}");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔧 [STOPWATCH] Stopwatch.StartNew完了 - インデックス: {chunk.StartIndex}-{chunk.EndIndex}{Environment.NewLine}");
            
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
                
                // 🚀 [TRUE_BATCH_PROCESSING] 真のバッチ翻訳実装 - GPU最適化されたバッチ推論を活用
                Console.WriteLine($"🚀 [TRUE_BATCH_PROCESSING] チャンクバッチ翻訳開始 - テキスト数: {chunkTexts.Count}");
                
                // チャンク全体を一度にバッチ翻訳で処理（個別翻訳から真のバッチ推論へ移行）
                var batchTranslationResults = await _translationService.TranslateBatchAsync(
                    chunkTexts.AsReadOnly(),
                    sourceLanguage,
                    targetLanguage,
                    null, // context
                    combinedCts.Token).ConfigureAwait(false);
                
                Console.WriteLine($"✅ [TRUE_BATCH_PROCESSING] バッチ翻訳完了 - 結果数: {batchTranslationResults.Count}");
                
                // バッチ翻訳結果を個別インデックスに配置し、コールバック通知
                for (int j = 0; j < chunkTexts.Count; j++)
                {
                    var textIndex = chunk.StartIndex + j;
                    var translationResult = j < batchTranslationResults.Count ? batchTranslationResults[j] : null;
                    var translatedText = translationResult?.IsSuccess == true ? translationResult.TranslatedText : chunkTexts[j];
                    
                    results[textIndex] = translatedText;
                    Console.WriteLine($"📢 [TRUE_BATCH_PROCESSING] バッチ翻訳完了通知 - インデックス: {textIndex}, 成功: {translationResult?.IsSuccess}");
                    onChunkCompleted?.Invoke(textIndex, translatedText);
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
                _logger.LogWarning("🚀 [TRUE_BATCH_PROCESSING] チャンクバッチ翻訳タイムアウト/キャンセル - チャンク: {Start}-{End}, エラー: {Error}", 
                    chunk.StartIndex, chunk.EndIndex, ex.Message);
                    
                // エラー時はプレースホルダーを設定
                for (int j = 0; j < chunk.Texts.Count; j++)
                {
                    results[chunk.StartIndex + j] = $"[バッチ翻訳タイムアウト] {chunk.Texts[j]}";
                    onChunkCompleted?.Invoke(chunk.StartIndex + j, results[chunk.StartIndex + j]);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "🚀 [TRUE_BATCH_PROCESSING] チャンクバッチ翻訳エラー - チャンク: {Start}-{End}", 
                    chunk.StartIndex, chunk.EndIndex);
                    
                // エラー時はプレースホルダーを設定
                for (int j = 0; j < chunk.Texts.Count; j++)
                {
                    results[chunk.StartIndex + j] = $"[バッチ翻訳エラー] {chunk.Texts[j]}";
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
        public List<string> Texts { get; set; } = [];
    }
}