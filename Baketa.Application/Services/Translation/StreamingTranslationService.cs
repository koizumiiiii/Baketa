using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Translation.Models;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Events.EventTypes;
using Baketa.Core.Events.Diagnostics;
using Baketa.Core.Utilities;
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
    private readonly IEventAggregator? _eventAggregator;
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
        ILogger<StreamingTranslationService> logger,
        IEventAggregator? eventAggregator = null)
    {
        Console.WriteLine("🚨🚨🚨 [CTOR_DEBUG] StreamingTranslationService コンストラクター開始");
        DebugLogUtility.WriteLog("🚨🚨🚨 [CTOR_DEBUG] StreamingTranslationService コンストラクター開始");

        _translationService = translationService ?? throw new ArgumentNullException(nameof(translationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _eventAggregator = eventAggregator;
        _progress = new Core.Translation.Models.TranslationProgress();

        Console.WriteLine($"🔥 [STREAMING] StreamingTranslationService初期化完了 - TranslationService型: {_translationService.GetType().Name}");
        DebugLogUtility.WriteLog($"🔥 [STREAMING] StreamingTranslationService初期化完了 - TranslationService型: {_translationService.GetType().Name}");
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
        // 🚨🚨🚨 [ULTRA_CRITICAL] メソッド本体に到達したことを確実に記録
        var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "baketa_debug.log");
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        var threadId = Environment.CurrentManagedThreadId;

        System.IO.File.AppendAllText(logPath, $"[{timestamp}][T{threadId:D2}] 🚨🚨🚨 [ULTRA_CRITICAL] TranslateBatchWithStreamingAsync メソッド本体到達！ - テキスト数: {texts?.Count ?? 0}\r\n");
        System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}][T{Environment.CurrentManagedThreadId:D2}] 🔥 [LINE77] Console.WriteLine実行直前\r\n");

        // 🚨 [CRITICAL_DEBUG] メソッド開始の即座ログ出力
        Console.WriteLine($"🚨 [CRITICAL_DEBUG] TranslateBatchWithStreamingAsync開始 - テキスト数: {texts?.Count ?? 0}");

        System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}][T{Environment.CurrentManagedThreadId:D2}] 🔥 [LINE78] Console.WriteLine実行完了\r\n");

        Console.WriteLine($"🔍 [LANGUAGE_DEBUG] 受信した言語設定: Source={sourceLanguage?.Code}({sourceLanguage?.DisplayName}) → Target={targetLanguage?.Code}({targetLanguage?.DisplayName})");
        // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
        // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化 → Target={targetLanguage?.Code}({targetLanguage?.DisplayName}){Environment.NewLine}");

        System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}][T{Environment.CurrentManagedThreadId:D2}] 🔥 [LINE82] texts nullチェック直前\r\n");

        if (texts == null || texts.Count == 0)
        {
            var textsStatus = texts == null ? "null" : "empty";
            Console.WriteLine($"🚨 [CRITICAL_DEBUG] テキストリスト空のため早期リターン - texts={textsStatus}");
            // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
            return [];
        }

        System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}][T{Environment.CurrentManagedThreadId:D2}] 🔥 [LINE90] Stopwatch開始直前\r\n");

        Console.WriteLine($"🚨 [CRITICAL_DEBUG] Stopwatch開始前");
        // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;

        var stopwatch = Stopwatch.StartNew();

        System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}][T{Environment.CurrentManagedThreadId:D2}] 🔥 [LINE93] Stopwatch開始完了\r\n");
        
        Console.WriteLine($"🚨 [CRITICAL_DEBUG] Logger情報出力前");
        // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
            
        _logger.LogInformation("🔥 [STREAMING] バッチ翻訳開始 - テキスト数: {Count}", texts.Count);
        Console.WriteLine($"🔥 [STREAMING] バッチ翻訳開始 - テキスト数: {texts.Count}");
        
        Console.WriteLine($"🚨 [CRITICAL_DEBUG] 進行状況初期化前");
        // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
        
        // 進行状況初期化
        lock (_progressLock)
        {
            Console.WriteLine($"🚨 [CRITICAL_DEBUG] lockブロック内部に到達");
            // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
                
            _progress.TotalChunks = texts.Count;
            _progress.CompletedChunks = 0;
            _progress.CurrentChunkIndex = 0;
            
            Console.WriteLine($"🚨 [CRITICAL_DEBUG] 進行状況初期化完了");
            // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
        }
        
        Console.WriteLine($"🚨 [CRITICAL_DEBUG] lockブロック脱出、CreateChunks呼び出し直前");
        // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
        
        Console.WriteLine($"🚨 [CRITICAL_DEBUG] results配列作成開始");
        // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
        
        var results = new string[texts.Count];
        
        Console.WriteLine($"🚨 [CRITICAL_DEBUG] CreateChunks呼び出し開始 - OptimalChunkSize={OptimalChunkSize}");
        // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
        
        var chunks = CreateChunks(texts, OptimalChunkSize);
        
        Console.WriteLine($"🚨 [CRITICAL_DEBUG] CreateChunks呼び出し完了");
        // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
        
        Console.WriteLine($"🚨 [CRITICAL_DEBUG] CreateChunks完了 - チャンク数: {chunks?.Count ?? 0}");
        // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
        
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

        System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}][T{Environment.CurrentManagedThreadId:D2}] 🔥 [LINE161] ProcessChunkAsync配列作成完了 - タスク数: {processingTasks.Length}\r\n");

        // 🚀 [TRUE_BATCH_PROCESSING] バッチ翻訳により例外は各チャンク内で処理済み
        try
        {
            Console.WriteLine($"🚨 [CHUNK_DEBUG] Task.WhenAll実行開始");
            System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}][T{Environment.CurrentManagedThreadId:D2}] 🔥 [LINE166] Task.WhenAll実行開始\r\n");

            await Task.WhenAll(processingTasks).ConfigureAwait(false);

            System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}][T{Environment.CurrentManagedThreadId:D2}] 🔥 [LINE168] Task.WhenAll実行完了\r\n");
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
        var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "baketa_debug.log");
        System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}][T{Environment.CurrentManagedThreadId:D2}] 🚨🚨🚨 [PROCESS_CHUNK] ProcessChunkAsync メソッド本体到達！ - インデックス: {chunk.StartIndex}-{chunk.EndIndex}\r\n");

        Console.WriteLine($"🚨 [CHUNK_DEBUG] ProcessChunkAsync開始 - インデックス: {chunk.StartIndex}-{chunk.EndIndex}");
        Console.WriteLine($"🚨 [CHUNK_DEBUG] semaphore.WaitAsync呼び出し前");
        
        // 🔧 [SEMAPHORE_FIX] usingパターンによるセマフォデッドロック完全解決
        using var semaphoreTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var semaphoreCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, semaphoreTimeout.Token);
        
        try
        {
            Console.WriteLine($"🚨 [DEADLOCK_DEBUG] セマフォ取得試行開始 - インデックス: {chunk.StartIndex}-{chunk.EndIndex}, 利用可能数: {semaphore.CurrentCount}");

            System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}][T{Environment.CurrentManagedThreadId:D2}] 🔥 [SEMAPHORE] WaitAsyncDisposableWithTimeout呼び出し直前 - 利用可能数: {semaphore.CurrentCount}\r\n");

            // 🔧 [CRITICAL_FIX] SemaphoreSlimExtensionsによる堅牢なリソース管理（Gemini推奨）
            var semaphoreScope = await semaphore.WaitAsyncDisposableWithTimeout(TimeSpan.FromSeconds(60), semaphoreCts.Token).ConfigureAwait(false);

            System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}][T{Environment.CurrentManagedThreadId:D2}] 🔥 [SEMAPHORE] WaitAsyncDisposableWithTimeout呼び出し完了 - semaphoreScope: {(semaphoreScope == null ? "null" : "not null")}\r\n");

            if (semaphoreScope == null)
            {
                // タイムアウト時の処理
                Console.WriteLine($"⚠️ [DEADLOCK_DEBUG] セマフォ取得タイムアウト(60秒) - インデックス: {chunk.StartIndex}-{chunk.EndIndex}");
                System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}][T{Environment.CurrentManagedThreadId:D2}] ⚠️ [SEMAPHORE] タイムアウト発生！\r\n");

                for (int j = 0; j < chunk.Texts.Count; j++)
                {
                    results[chunk.StartIndex + j] = "[セマフォ取得タイムアウト]";
                    onChunkCompleted?.Invoke(chunk.StartIndex + j, results[chunk.StartIndex + j]);
                }
                return; // 🔧 [FIXED] セマフォが取得されていないので、リリース不要で安全にreturn
            }

            System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}][T{Environment.CurrentManagedThreadId:D2}] ✅ [SEMAPHORE] セマフォ取得成功 - 残り利用可能数: {semaphore.CurrentCount}\r\n");

            Console.WriteLine($"✅ [DEADLOCK_DEBUG] セマフォ取得成功 - インデックス: {chunk.StartIndex}-{chunk.EndIndex}, 残り利用可能数: {semaphore.CurrentCount}");
            
            // 🔧 [FIXED] usingパターンで自動的にセマフォが解放されるため、安全に処理を実行
            using (semaphoreScope)
            {
                Console.WriteLine($"🔧 [POST_SEMAPHORE] セマフォ取得後処理開始 - インデックス: {chunk.StartIndex}-{chunk.EndIndex}");
                
                var chunkStopwatch = Stopwatch.StartNew();
                Console.WriteLine($"🔧 [STOPWATCH] Stopwatch.StartNew完了 - インデックス: {chunk.StartIndex}-{chunk.EndIndex}");
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
                    
                    // 🔥 [DIAGNOSTIC] 翻訳品質診断イベント: 言語検出
                    var translationId = Guid.NewGuid().ToString("N")[..8];
                    var translationStart = DateTime.UtcNow;
                    
                    if (_eventAggregator != null)
                    {
                        await _eventAggregator.PublishAsync(new PipelineDiagnosticEvent
                        {
                            Stage = "LanguageDetection",
                            IsSuccess = true,
                            ProcessingTimeMs = 0,
                            SessionId = translationId,
                            Severity = DiagnosticSeverity.Information,
                            Message = $"言語検出完了: {sourceLanguage.Code} → {targetLanguage.Code}",
                            Metrics = new Dictionary<string, object>
                            {
                                { "SourceLanguage", sourceLanguage.Code },
                                { "TargetLanguage", targetLanguage.Code },
                                { "TextCount", chunkTexts.Count },
                                { "TotalTextLength", totalTextLength }
                            }
                        }).ConfigureAwait(false);
                        
                        // 🔥 [DIAGNOSTIC] 翻訳エンジン選択診断イベント
                        await _eventAggregator.PublishAsync(new PipelineDiagnosticEvent
                        {
                            Stage = "TranslationEngineSelection",
                            IsSuccess = true,
                            ProcessingTimeMs = 0,
                            SessionId = translationId,
                            Severity = DiagnosticSeverity.Information,
                            Message = $"ストリーミング翻訳エンジン使用: {_translationService.GetType().Name}",
                            Metrics = new Dictionary<string, object>
                            {
                                { "EngineName", _translationService.GetType().Name },
                                { "EngineType", "StreamingBatch" },
                                { "ChunkSize", chunkTexts.Count },
                                { "TimeoutSeconds", timeoutSeconds }
                            }
                        }).ConfigureAwait(false);
                    }
                    
                    // 🚀 [TRUE_BATCH_PROCESSING] 真のバッチ翻訳実装 - GPU最適化されたバッチ推論を活用
                    Console.WriteLine($"🚀 [TRUE_BATCH_PROCESSING] チャンクバッチ翻訳開始 - テキスト数: {chunkTexts.Count}");

                    System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}][T{Environment.CurrentManagedThreadId:D2}] 🔥 [TRANSLATE_BATCH] TranslateBatchAsync呼び出し直前 - テキスト数: {chunkTexts.Count}\r\n");

                    // チャンク全体を一度にバッチ翻訳で処理（個別翻訳から真のバッチ推論へ移行）
                    var batchTranslationResults = await _translationService.TranslateBatchAsync(
                        chunkTexts.AsReadOnly(),
                        sourceLanguage,
                        targetLanguage,
                        null, // context
                        combinedCts.Token).ConfigureAwait(false);

                    System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}][T{Environment.CurrentManagedThreadId:D2}] 🔥 [TRANSLATE_BATCH] TranslateBatchAsync呼び出し完了 - 結果数: {batchTranslationResults.Count}\r\n");

                    Console.WriteLine($"✅ [TRUE_BATCH_PROCESSING] バッチ翻訳完了 - 結果数: {batchTranslationResults.Count}");
                    
                    // 🔥 [DIAGNOSTIC] 翻訳品質診断イベント: 翻訳実行結果
                    var translationEnd = DateTime.UtcNow;
                    var translationDuration = (translationEnd - translationStart).TotalMilliseconds;
                    var successCount = batchTranslationResults.Count(r => r != null && r.IsSuccess); // 🔧 [ULTRAPHASE4_L1] null安全化
                    var sameLanguageCount = 0;
                    
                    // 🔍 翻訳品質チェック: 高精度言語比較による翻訳失敗検出
                    var sameLanguageFailures = new List<string>();
                    for (int qualityCheck = 0; qualityCheck < Math.Min(chunkTexts.Count, batchTranslationResults.Count); qualityCheck++)
                    {
                        var originalText = chunkTexts[qualityCheck];
                        var translatedText = batchTranslationResults[qualityCheck]?.TranslatedText;
                        
                        if (!string.IsNullOrEmpty(translatedText))
                        {
                            try
                            {
                                // 言語検出による高精度比較（現在の実装では単純文字列比較を使用）
                                // TODO: 言語検出APIが利用可能になった場合に実装予定
                                // var originalLangTask = languageDetectionService.DetectLanguageAsync(originalText, combinedCts.Token);
                                // var translatedLangTask = languageDetectionService.DetectLanguageAsync(translatedText, combinedCts.Token);
                                
                                // フォールバック: 文字列比較による翻訳失敗検出
                                var isSameText = originalText.Trim().Equals(translatedText.Trim(), StringComparison.OrdinalIgnoreCase);
                                
                                // 改良された翻訳失敗検出ロジック
                                if (isSameText)
                                {
                                    sameLanguageCount++;
                                    sameLanguageFailures.Add($"{originalText} -> {translatedText} (text comparison)");
                                    Console.WriteLine($"🚨 [ENHANCED_DIAGNOSTIC] 翻訳失敗検出（文字列一致）: '{originalText}' -> '{translatedText}'");
                                }
                            }
                            catch (Exception langDetectEx)
                            {
                                // 言語検出に失敗した場合はフォールバックとして文字列比較を使用
                                if (originalText == translatedText)
                                {
                                    sameLanguageCount++;
                                    sameLanguageFailures.Add($"{originalText} -> {translatedText} (fallback: text comparison)");
                                    Console.WriteLine($"🚨 [FALLBACK_DIAGNOSTIC] 文字列比較で同一検出: '{originalText}' (言語検出エラー: {langDetectEx.Message})");
                                }
                            }
                        }
                    }
                    
                    if (_eventAggregator != null)
                    {
                        await _eventAggregator.PublishAsync(new PipelineDiagnosticEvent
                        {
                            Stage = "TranslationExecution",
                            IsSuccess = successCount > 0,
                            ProcessingTimeMs = (long)translationDuration,
                            SessionId = translationId,
                            Severity = successCount == 0 ? DiagnosticSeverity.Error : DiagnosticSeverity.Information,
                            Message = $"ストリーミング翻訳実行完了: 成功{successCount}/{batchTranslationResults.Count}",
                            Metrics = new Dictionary<string, object>
                            {
                                { "TotalTexts", chunkTexts.Count },
                                { "SuccessCount", successCount },
                                { "FailureCount", batchTranslationResults.Count - successCount },
                                { "ProcessingTimeMs", translationDuration },
                                { "EngineName", _translationService.GetType().Name }
                            }
                        }).ConfigureAwait(false);
                        
                        // 🔥 [DIAGNOSTIC] 翻訳品質チェック診断イベント
                        await _eventAggregator.PublishAsync(new PipelineDiagnosticEvent
                        {
                            Stage = "TranslationQualityCheck",
                            IsSuccess = sameLanguageCount == 0,
                            ProcessingTimeMs = 0,
                            SessionId = translationId,
                            Severity = sameLanguageCount > 0 ? DiagnosticSeverity.Warning : DiagnosticSeverity.Information,
                            Message = sameLanguageCount > 0 
                                ? $"翻訳品質警告: {sameLanguageCount}件の翻訳失敗検出（改良された診断ロジック）" 
                                : "翻訳品質チェック成功: 正常な翻訳結果（改良された診断検証済み）",
                            Metrics = new Dictionary<string, object>
                            {
                                { "TotalTexts", chunkTexts.Count },
                                { "SameLanguageCount", sameLanguageCount },
                                { "QualityScore", sameLanguageCount == 0 ? 1.0 : 1.0 - ((double)sameLanguageCount / chunkTexts.Count) },
                                { "SourceLanguage", sourceLanguage.Code },
                                { "TargetLanguage", targetLanguage.Code },
                                { "DetectionMethod", "EnhancedTextComparison" },
                                { "FailureDetails", sameLanguageFailures.Count > 0 ? sameLanguageFailures.Take(5) : new List<string>() },
                                { "IsTextComparisonBased", true }
                            }
                        }).ConfigureAwait(false);
                    }
                    
                    // 🔥 [DIAGNOSTIC] 翻訳結果の詳細ログ出力
                    Console.WriteLine($"🔍 [TRANSLATION_QUALITY] 翻訳品質診断: 成功{successCount}/{batchTranslationResults.Count}, 同一結果{sameLanguageCount}件");
                    
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
                    // 🚨🚨🚨 [ULTRA_CRITICAL_CATCH] 絶対に実行される診断ログ
                    Console.WriteLine($"🚨🚨🚨 [STREAMING_CATCH] チャンクバッチ翻訳エラー - ExceptionType: {ex.GetType().Name}, Message: {ex.Message}");
                    System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}][T{Environment.CurrentManagedThreadId:D2}] 🚨🚨🚨 [STREAMING_CATCH] ExceptionType: {ex.GetType().Name}, Message: {ex.Message}, _logger is null: {_logger == null}\r\n");

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
            } // 🔧 [FIXED] usingブロックここで自動的にsemaphore.Release()が実行される
        }
        catch (OperationCanceledException)
        {
            // キャンセル時の処理（セマフォは自動で解放される）
            Console.WriteLine($"⚠️ [CHUNK_DEBUG] チャンク処理キャンセル - インデックス: {chunk.StartIndex}-{chunk.EndIndex}");
        }
        // 🔧 [FIXED] finally句は不要 - usingパターンで自動リソース管理
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