# 翻訳精度向上のための実装戦略書

## 📋 **エグゼクティブサマリー**

現状のBaketaアーキテクチャは座標処理・テキスト管理において非常に優秀ですが、**時間軸でのチャンク統合機能**が欠如しており、これが翻訳品質のボトルネックとなっています。

この文書では、翻訳精度向上のための3つの改善項目を**実装優先度順**で提示し、具体的な実装方針を示します。

---

## 🎯 **改善項目と期待効果**

| 優先度 | 項目 | 期待効果 | 実装難易度 | 実装期間 |
|--------|------|----------|------------|----------|
| 🔴 **最優先** | TimedChunkAggregator | 翻訳品質40-60%向上 | 中 | 1週間 |
| 🟡 **高優先** | 強化ノイズ除去統合 | OCR誤認識大幅削減 | 低 | 3日 |
| 🟢 **中優先** | 言語特化処理（拡張設計） | 多言語対応・自然性向上 | 低 | 2日 |

---

## 📊 **現状分析結果**

### ✅ **優秀な既存実装**
- `TextChunk`クラス：座標・テキスト管理は提案要件を上回るレベル
- `CoordinateBasedLineBreakProcessor`：高度な座標ベース統合処理
- `LanguagePairSelectionViewModel`：完成されたユーザー設定管理

### 🔴 **核心問題の特定**
**時間軸バッファリング層の完全欠如**が翻訳品質向上の最大のボトルネック

```
現状のフロー（問題）:
OCR検出 → 即座に翻訳処理 → 個別表示
↓ 結果：文脈を失った分割翻訳

理想のフロー（改善後）:  
OCR検出 → 150ms待機 → 複数チャンク統合 → 一括翻訳 → 統合表示
↓ 結果：文脈考慮の高品質翻訳
```

---

## 🔴 **最優先：TimedChunkAggregator実装**

### **💡 目的・効果**
現状の「検出即翻訳」から「150ms待機→統合翻訳」への転換で、文脈を考慮した高品質翻訳を実現。

### **🏗️ アーキテクチャ設計**

#### **実装場所**
```
Baketa.Infrastructure/OCR/Processing/TimedChunkAggregator.cs
```

#### **統合ポイント**
既存の`BatchOcrIntegrationService`と連携し、OCR結果を受け取った直後に集約処理を挟む。

### **💻 具体的実装設計**

#### **核心クラス：TimedChunkAggregator**

```csharp
namespace Baketa.Infrastructure.OCR.Processing;

/// <summary>
/// 時間軸ベースのTextChunk集約処理クラス
/// OCR結果を一定時間バッファリングし、統合してから翻訳パイプラインに送信
/// Geminiフィードバック反映: SourceWindowHandle別バッファ管理、ForceFlushMs制御強化
/// </summary>
public sealed class TimedChunkAggregator : IDisposable
{
    private readonly Timer _aggregationTimer;
    private readonly Dictionary<IntPtr, List<TextChunk>> _pendingChunksByWindow;
    private readonly SemaphoreSlim _processingLock;
    private readonly ILogger<TimedChunkAggregator> _logger;
    private readonly CoordinateBasedLineBreakProcessor _lineBreakProcessor;
    
    // 設定可能なバッファ時間（デフォルト150ms）
    private readonly int _bufferDelayMs;
    private readonly int _maxChunkCount;
    private readonly int _forceFlushMs;
    private readonly bool _isFeatureEnabled;
    
    // パフォーマンス監視用
    private long _totalChunksProcessed;
    private long _totalAggregationEvents;
    private readonly Stopwatch _performanceStopwatch;
    private readonly DateTime _lastTimerReset;
    private volatile int _nextChunkId;
    
    public TimedChunkAggregator(
        TimedAggregatorSettings settings,
        CoordinateBasedLineBreakProcessor lineBreakProcessor,
        ILogger<TimedChunkAggregator> logger)
    {
        _bufferDelayMs = settings.BufferDelayMs;
        _maxChunkCount = settings.MaxChunkCount;
        _forceFlushMs = settings.ForceFlushMs;
        _isFeatureEnabled = settings.IsFeatureEnabled;
        _lineBreakProcessor = lineBreakProcessor;
        _pendingChunksByWindow = new Dictionary<IntPtr, List<TextChunk>>();
        _processingLock = new SemaphoreSlim(1, 1);
        _logger = logger;
        _performanceStopwatch = new Stopwatch();
        _lastTimerReset = DateTime.UtcNow;
        _nextChunkId = Random.Shared.Next(1000000, 9999999);
        
        _aggregationTimer = new Timer(ProcessPendingChunks, null, 
            Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// 新しいチャンクを追加し、タイマーをリセット
    /// Geminiフィードバック反映: SourceWindowHandle別管理、ForceFlushMs制御
    /// </summary>
    public async Task<bool> TryAddChunkAsync(TextChunk chunk, CancellationToken cancellationToken = default)
    {
        // Feature Flag チェック - 機能が無効の場合は即座にfalseを返す
        if (!_isFeatureEnabled)
        {
            _logger.LogDebug("TimedChunkAggregator機能が無効化されています");
            return false;
        }

        await _processingLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // パフォーマンス計測開始
            _performanceStopwatch.Start();
            
            // SourceWindowHandle別にバッファを分離（コンテキスト混在防止）
            var windowHandle = chunk.SourceWindowHandle;
            if (!_pendingChunksByWindow.ContainsKey(windowHandle))
            {
                _pendingChunksByWindow[windowHandle] = new List<TextChunk>();
            }
            
            _pendingChunksByWindow[windowHandle].Add(chunk);
            Interlocked.Increment(ref _totalChunksProcessed);
            
            // 全ウィンドウのチャンク数を計算
            var totalChunks = _pendingChunksByWindow.Values.Sum(list => list.Count);
            
            // メモリ保護：最大チャンク数を超えたら強制処理
            if (totalChunks >= _maxChunkCount)
            {
                _logger.LogWarning("最大チャンク数到達 - 強制処理開始: {Count}個", totalChunks);
                await ProcessPendingChunksInternal().ConfigureAwait(false);
                return true;
            }
            
            // ForceFlushMs制御: 無限タイマーリセットを防ぐ
            var timeSinceLastReset = DateTime.UtcNow - _lastTimerReset;
            if (timeSinceLastReset.TotalMilliseconds >= _forceFlushMs)
            {
                _logger.LogDebug("ForceFlushMs到達 - 強制処理実行: {ElapsedMs}ms経過", timeSinceLastReset.TotalMilliseconds);
                await ProcessPendingChunksInternal().ConfigureAwait(false);
            }
            else
            {
                // タイマーをリセット（新しいチャンクが来たら待ち時間をリセット）
                _aggregationTimer.Change(_bufferDelayMs, Timeout.Infinite);
                _lastTimerReset = DateTime.UtcNow; // タイマーリセット時刻を記録
            }
            
            _logger.LogDebug("チャンク追加 - ウィンドウ: {WindowHandle}, 合計: {Count}個, 次回処理: {DelayMs}ms後", 
                windowHandle, totalChunks, _bufferDelayMs);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "チャンク追加処理中にエラーが発生: ChunkId={ChunkId}, WindowHandle={WindowHandle}", 
                chunk?.ChunkId, chunk?.SourceWindowHandle);
            throw;
        }
        finally
        {
            _performanceStopwatch.Stop();
            _processingLock.Release();
        }
    }

    /// <summary>
    /// バッファされたチャンクを統合処理（タイマーコールバック）
    /// Geminiフィードバック反映: 包括的エラーハンドリング
    /// </summary>
    private async void ProcessPendingChunks(object? state)
    {
        try
        {
            await _processingLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await ProcessPendingChunksInternal().ConfigureAwait(false);
            }
            finally
            {
                _processingLock.Release();
            }
        }
        catch (Exception ex)
        {
            // async void methodの例外は適切にログ出力（アプリケーション終了を防ぐ）
            _logger.LogError(ex, "ProcessPendingChunks（タイマーコールバック）でエラーが発生");
        }
    }

    /// <summary>
    /// 内部統合処理
    /// Geminiフィードバック反映: ウィンドウハンドル別処理
    /// </summary>
    private async Task ProcessPendingChunksInternal()
    {
        if (_pendingChunksByWindow.Count == 0) return;

        // 全ウィンドウのチャンクを取得してクリア
        var chunksToProcessByWindow = new Dictionary<IntPtr, List<TextChunk>>();
        foreach (var kvp in _pendingChunksByWindow)
        {
            chunksToProcessByWindow[kvp.Key] = kvp.Value.ToList();
        }
        _pendingChunksByWindow.Clear();
        
        var totalInputChunks = chunksToProcessByWindow.Values.Sum(list => list.Count);
        _logger.LogDebug("統合処理開始 - {WindowCount}ウィンドウ, {Count}個のチャンク", 
            chunksToProcessByWindow.Count, totalInputChunks);

        try
        {
            var allAggregatedChunks = new List<TextChunk>();
            
            // ウィンドウハンドル別に統合処理（コンテキスト分離）
            foreach (var kvp in chunksToProcessByWindow)
            {
                var windowHandle = kvp.Key;
                var chunksForWindow = kvp.Value;
                
                if (chunksForWindow.Count > 0)
                {
                    var aggregatedChunks = CombineChunks(chunksForWindow);
                    allAggregatedChunks.AddRange(aggregatedChunks);
                    
                    _logger.LogDebug("ウィンドウ {WindowHandle}: {InputCount}個→{OutputCount}個のチャンク統合",
                        windowHandle, chunksForWindow.Count, aggregatedChunks.Count);
                }
            }
            
            // 統合されたチャンクを翻訳パイプラインに送信
            if (OnChunksAggregated != null && allAggregatedChunks.Count > 0)
            {
                await OnChunksAggregated.Invoke(allAggregatedChunks).ConfigureAwait(false);
            }
            
            _logger.LogDebug("統合処理完了 - {InputCount}個→{OutputCount}個のチャンク", 
                totalInputChunks, allAggregatedChunks.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "チャンク統合処理中にエラーが発生");
            throw;
        }
    }

    /// <summary>
    /// 複数チャンクを統合（既存のCoordinateBasedLineBreakProcessorを活用）
    /// </summary>
    private List<TextChunk> CombineChunks(List<TextChunk> chunks)
    {
        if (chunks.Count == 0) return new List<TextChunk>();
        if (chunks.Count == 1) return chunks;

        // 座標ベースでグループ化・統合
        var combinedText = _lineBreakProcessor.ProcessLineBreaks(chunks);
        
        // 統合されたテキストから新しいTextChunkを作成
        var combinedBounds = CalculateCombinedBounds(chunks);
        var combinedChunk = new TextChunk
        {
            ChunkId = GenerateNewChunkId(),
            TextResults = chunks.SelectMany(c => c.TextResults).ToList(),
            CombinedBounds = combinedBounds,
            CombinedText = combinedText,
            SourceWindowHandle = chunks[0].SourceWindowHandle,
            DetectedLanguage = chunks[0].DetectedLanguage
        };

        return new List<TextChunk> { combinedChunk };
    }

    /// <summary>
    /// 統合されたバウンディングボックスを計算
    /// </summary>
    private Rectangle CalculateCombinedBounds(List<TextChunk> chunks)
    {
        if (chunks.Count == 0) return Rectangle.Empty;
        if (chunks.Count == 1) return chunks[0].CombinedBounds;

        var minX = chunks.Min(c => c.CombinedBounds.X);
        var minY = chunks.Min(c => c.CombinedBounds.Y);
        var maxRight = chunks.Max(c => c.CombinedBounds.Right);
        var maxBottom = chunks.Max(c => c.CombinedBounds.Bottom);

        return new Rectangle(minX, minY, maxRight - minX, maxBottom - minY);
    }

    /// <summary>
    /// 新しいChunkIDを生成
    /// Geminiフィードバック反映: スレッドセーフなID生成
    /// </summary>
    private int GenerateNewChunkId()
    {
        return Interlocked.Increment(ref _nextChunkId);
    }

    /// <summary>
    /// 集約完了イベント
    /// </summary>
    public Func<List<TextChunk>, Task>? OnChunksAggregated { get; set; }

    public void Dispose()
    {
        _aggregationTimer?.Dispose();
        _processingLock?.Dispose();
        _logger?.LogDebug("TimedChunkAggregator disposed");
    }
}
```

#### **設定クラス**

```csharp
/// <summary>
/// TimedChunkAggregatorの設定クラス
/// Geminiフィードバックを反映した拡張版
/// </summary>
public sealed class TimedAggregatorSettings
{
    /// <summary>バッファ待機時間（ms）- デフォルト150ms</summary>
    public int BufferDelayMs { get; init; } = 150;
    
    /// <summary>最大チャンク数（メモリ保護）- デフォルト50個</summary>
    public int MaxChunkCount { get; init; } = 50;
    
    /// <summary>強制フラッシュ時間（ms）- デフォルト1000ms</summary>
    public int ForceFlushMs { get; init; } = 1000;
    
    /// <summary>Feature Flag - 機能の段階的導入用</summary>
    public bool IsFeatureEnabled { get; init; } = true;
    
    /// <summary>パフォーマンスログ出力有無</summary>
    public bool EnablePerformanceLogging { get; init; } = false;
    
    /// <summary>ソースウィンドウハンドル別処理有無（Geminiフィードバック反映）</summary>
    public bool SeparateBySourceWindow { get; init; } = true;
    
    /// <summary>ユーザー設定からの読み込みコンストラクタ</summary>
    public static TimedAggregatorSettings FromUserSettings(/* ユーザー設定インターface */)
    {
        var settings = new TimedAggregatorSettings
        {
            // 初期段階ではFeature Flagをfalseに設定し、段階的に有効化
            IsFeatureEnabled = false, // リリース後にtrueに変更
            EnablePerformanceLogging = true, // 初期モニタリング有効
        };
        settings.Validate();
        return settings;
    }
    
    /// <summary>開発環境用設定</summary>
    public static TimedAggregatorSettings Development => new()
    {
        BufferDelayMs = 100, // 開発時は短めのバッファ
        IsFeatureEnabled = true,
        EnablePerformanceLogging = true,
    };
    
    /// <summary>本番環境用設定</summary>
    public static TimedAggregatorSettings Production => new()
    {
        BufferDelayMs = 150,
        IsFeatureEnabled = false, // 最初は無効化して段階的に有効化
        EnablePerformanceLogging = false,
    };
    
    /// <summary>設定検証</summary>
    public void Validate()
    {
        if (BufferDelayMs < 10 || BufferDelayMs > 5000)
            throw new ArgumentOutOfRangeException(nameof(BufferDelayMs), "バッファ時間は10-5000msの範囲で設定してください");
            
        if (MaxChunkCount < 1 || MaxChunkCount > 500)
            throw new ArgumentOutOfRangeException(nameof(MaxChunkCount), "最大チャンク数は1-500個の範囲で設定してください");
    }
}
```

### **🔗 既存システムとの統合**

#### **BatchOcrIntegrationServiceの拡張**

```csharp
/// <summary>
/// 時間軸統合機能を備えた強化版BatchOcrIntegrationService
/// </summary>
public sealed class EnhancedBatchOcrIntegrationService : IDisposable
{
    private readonly IBatchOcrProcessor _batchOcrProcessor;
    private readonly TimedChunkAggregator _chunkAggregator;
    private readonly ITranslationPipelineService _translationPipeline;
    private readonly ILogger<EnhancedBatchOcrIntegrationService> _logger;
    
    public EnhancedBatchOcrIntegrationService(
        IBatchOcrProcessor batchOcrProcessor,
        TimedChunkAggregator chunkAggregator,
        ITranslationPipelineService translationPipeline,
        ILogger<EnhancedBatchOcrIntegrationService> logger)
    {
        _batchOcrProcessor = batchOcrProcessor;
        _chunkAggregator = chunkAggregator;
        _translationPipeline = translationPipeline;
        _logger = logger;
        
        // 集約完了時の処理をセット
        _chunkAggregator.OnChunksAggregated = OnAggregatedChunksReady;
    }

    /// <summary>
    /// バッファリング付きOCR処理
    /// </summary>
    public async Task<IReadOnlyList<TextChunk>> ProcessImageWithBufferingAsync(
        IImage image, IntPtr windowHandle, CancellationToken ct = default)
    {
        var chunks = await _batchOcrProcessor.ProcessAsync(image, ct).ConfigureAwait(false);
        
        // 従来：即座に翻訳処理
        // return chunks;
        
        // 新方式：バッファに追加（非同期で後に処理される）
        foreach (var chunk in chunks)
        {
            await _chunkAggregator.TryAddChunkAsync(chunk, ct).ConfigureAwait(false);
        }
        
        // 即座にはTextChunkを返さず、集約後にイベントで処理
        // UI層での待機が必要な場合は、TaskCompletionSourceを使用して同期化
        return Array.Empty<TextChunk>();
    }

    /// <summary>
    /// 集約されたチャンクの翻訳処理
    /// </summary>
    private async Task OnAggregatedChunksReady(List<TextChunk> aggregatedChunks)
    {
        try
        {
            _logger.LogDebug("集約チャンクを翻訳パイプラインに送信: {Count}個", aggregatedChunks.Count);
            
            // 翻訳パイプラインに送信
            foreach (var chunk in aggregatedChunks)
            {
                await _translationPipeline.ProcessChunkAsync(chunk).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "集約チャンクの翻訳処理中にエラーが発生");
        }
    }

    public void Dispose()
    {
        _chunkAggregator?.Dispose();
        _logger?.LogInformation("EnhancedBatchOcrIntegrationService disposed");
    }
}
```

---

## 🟡 **高優先：強化ノイズ除去統合**

### **💡 目的・効果**
装飾記号・誤認識文字の除去により、翻訳エンジンに渡されるテキスト品質を大幅向上。

### **🏗️ 実装場所**
既存の`CoordinateBasedLineBreakProcessor`を拡張

### **💻 具体的実装**

#### **AdvancedTextCleaner クラス**

```csharp
namespace Baketa.Infrastructure.OCR.PostProcessing;

/// <summary>
/// 強化されたテキストクリーニング処理
/// 装飾記号除去・言語特化処理・誤認識修正を統合
/// </summary>
public sealed class AdvancedTextCleaner
{
    private readonly ILogger<AdvancedTextCleaner> _logger;
    private readonly AdvancedCleaningSettings _settings;
    
    public AdvancedTextCleaner(
        AdvancedCleaningSettings settings,
        ILogger<AdvancedTextCleaner> logger)
    {
        _settings = settings;
        _logger = logger;
    }
    
    /// <summary>
    /// 強化されたテキストクリーニング
    /// </summary>
    public string CleanTextAdvanced(string text, string? detectedLanguage = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        
        var originalText = text;
        
        // 1. 装飾記号除去（提案書の核心要件）
        text = RemoveDecorationSymbols(text);
        
        // 2. 言語特化クリーニング
        text = ApplyLanguageSpecificCleaning(text, detectedLanguage);
        
        // 3. 一般的な誤認識修正
        text = CorrectCommonMisrecognitions(text);
        
        // 4. 不要な空白・改行の正規化
        text = NormalizeWhitespace(text);
        
        if (_settings.EnableVerboseLogging && originalText != text)
        {
            _logger.LogTrace("テキストクリーニング: '{Original}' → '{Cleaned}'", 
                originalText, text);
        }
        
        return text.Trim();
    }
    
    /// <summary>
    /// 装飾記号の除去（提案書で指摘された装飾記号を除去）
    /// Geminiフィードバック反映: Regexコンパイル最適化
    /// </summary>
    private static readonly Regex DecorationSymbolsRegex = new(@"[■◆│▲▼◀▶※]", RegexOptions.Compiled);
    
    private string RemoveDecorationSymbols(string text)
    {
        // 提案書で明示的に指摘された装飾記号を除去（コンパイル済みRegex使用）
        return DecorationSymbolsRegex.Replace(text, string.Empty);
    }
    
    /// <summary>
    /// 言語特化クリーニング
    /// </summary>
    private string ApplyLanguageSpecificCleaning(string text, string? language)
    {
        return language?.ToLowerInvariant() switch
        {
            "ja" or "jp" => CleanJapanese(text),
            "en" => CleanEnglish(text),
            "zh" or "zh-cn" or "zh-tw" => CleanChinese(text),
            _ => text // デフォルトはそのまま
        };
    }
    
    // Geminiフィードバック反映: Regexコンパイル最適化（日本語処理）
    private static readonly Regex JapaneseNewlineRegex = new(@"[\n\t\r]", RegexOptions.Compiled);
    private static readonly Regex JapaneseExclamationRegex = new(@"[!！]", RegexOptions.Compiled);
    private static readonly Regex JapaneseQuestionRegex = new(@"[?？]", RegexOptions.Compiled);
    private static readonly Regex JapaneseTildeRegex = new(@"[~～]", RegexOptions.Compiled);
    private static readonly Regex JapaneseWhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    
    /// <summary>
    /// 日本語特有のクリーニング
    /// Geminiフィードバック反映: コンパイル済みRegex使用でパフォーマンス向上
    /// </summary>
    private string CleanJapanese(string text)
    {
        // 日本語特有の不要文字除去（コンパイル済みRegex使用）
        text = JapaneseNewlineRegex.Replace(text, string.Empty);
        
        // 全角・半角統一（コンパイル済みRegex使用）
        text = JapaneseExclamationRegex.Replace(text, "！");
        text = JapaneseQuestionRegex.Replace(text, "？");
        text = JapaneseTildeRegex.Replace(text, "～");
        
        // 不要なスペースの除去（日本語では基本的にスペース不要）
        text = JapaneseWhitespaceRegex.Replace(text, string.Empty);
        
        return text;
    }
    
    // Geminiフィードバック反映: Regexコンパイル最適化（英語処理）
    private static readonly Regex EnglishNewlineRegex = new(@"[\n\t\r]", RegexOptions.Compiled);
    private static readonly Regex EnglishWhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex EnglishLowercaseLRegex = new(@"\bl\b", RegexOptions.Compiled);
    private static readonly Regex EnglishZeroRegex = new(@"\b0\b", RegexOptions.Compiled);
    
    /// <summary>
    /// 英語特有のクリーニング
    /// Geminiフィードバック反映: コンパイル済みRegex使用でパフォーマンス向上
    /// </summary>
    private string CleanEnglish(string text)
    {
        // 英語特有のスペース正規化（コンパイル済みRegex使用）
        text = EnglishNewlineRegex.Replace(text, " ");
        text = EnglishWhitespaceRegex.Replace(text, " ");
        
        // 一般的な誤認識修正（コンパイル済みRegex使用）
        text = EnglishLowercaseLRegex.Replace(text, "I"); // 小文字lを大文字Iに
        text = EnglishZeroRegex.Replace(text, "O"); // 数字0を文字Oに（文脈による）
        
        return text;
    }
    
    // Geminiフィードバック反映: Regexコンパイル最適化（中国語処理）
    private static readonly Regex ChineseNewlineRegex = new(@"[\n\t\r]", RegexOptions.Compiled);
    
    /// <summary>
    /// 中国語特有のクリーニング
    /// Geminiフィードバック反映: コンパイル済みRegex使用でパフォーマンス向上
    /// </summary>
    private string CleanChinese(string text)
    {
        // 中国語特有の処理（コンパイル済みRegex使用）
        text = ChineseNewlineRegex.Replace(text, string.Empty);
        return text;
    }
    
    /// <summary>
    /// 一般的な誤認識修正
    /// </summary>
    private string CorrectCommonMisrecognitions(string text)
    {
        // よくある誤認識パターンの修正
        var corrections = new Dictionary<string, string>
        {
            { "rn", "m" },      // "rn"を"m"に
            { "cl", "d" },      // "cl"を"d"に
            { "vv", "w" },      // "vv"を"w"に
        };
        
        foreach (var correction in corrections)
        {
            text = text.Replace(correction.Key, correction.Value);
        }
        
        return text;
    }
    
    // Geminiフィードバック反映: Regexコンパイル最適化（正規化処理）
    private static readonly Regex MultipleNewlinesRegex = new(@"\n{3,}", RegexOptions.Compiled);
    private static readonly Regex TrailingWhitespaceRegex = new(@"[ \t]+$", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex LeadingWhitespaceRegex = new(@"^[ \t]+", RegexOptions.Compiled | RegexOptions.Multiline);
    
    /// <summary>
    /// 空白・改行の正規化
    /// Geminiフィードバック反映: コンパイル済みRegex使用でパフォーマンス向上
    /// </summary>
    private string NormalizeWhitespace(string text)
    {
        // 連続する改行を単一に（コンパイル済みRegex使用）
        text = MultipleNewlinesRegex.Replace(text, "\n\n");
        
        // 行末・行頭の不要な空白を除去（コンパイル済みRegex使用）
        text = TrailingWhitespaceRegex.Replace(text, string.Empty);
        text = LeadingWhitespaceRegex.Replace(text, string.Empty);
        
        return text;
    }
}

/// <summary>
/// AdvancedTextCleanerの設定
/// </summary>
public sealed class AdvancedCleaningSettings
{
    public bool EnableVerboseLogging { get; init; } = false;
    public bool EnableLanguageSpecificCleaning { get; init; } = true;
    public bool EnableMisrecognitionCorrection { get; init; } = true;
    
    public static AdvancedCleaningSettings Default => new();
}
```

#### **CoordinateBasedLineBreakProcessorの拡張**

```csharp
/// <summary>
/// 強化されたテキストクリーニング機能を統合したCoordinateBasedLineBreakProcessor
/// </summary>
public sealed class CoordinateBasedLineBreakProcessor
{
    private readonly AdvancedTextCleaner _textCleaner;
    private readonly ILogger<CoordinateBasedLineBreakProcessor> _logger;
    private readonly LineBreakSettings _settings;
    
    public CoordinateBasedLineBreakProcessor(
        ILogger<CoordinateBasedLineBreakProcessor> logger,
        AdvancedTextCleaner textCleaner,
        LineBreakSettings? settings = null)
    {
        _logger = logger;
        _textCleaner = textCleaner;
        _settings = settings ?? LineBreakSettings.Default;
    }
    
    // 既存のMergeLineChunksメソッドを拡張
    private string MergeLineChunks(List<TextChunk> lineChunks)
    {
        if (lineChunks.Count == 0) return string.Empty;
        
        if (lineChunks.Count == 1)
        {
            // 🆕 強化クリーニングを適用
            var cleanedText = _textCleaner.CleanTextAdvanced(
                lineChunks[0].CombinedText, 
                lineChunks[0].DetectedLanguage);
            return cleanedText;
        }
        
        var result = new StringBuilder();
        
        for (int i = 0; i < lineChunks.Count; i++)
        {
            var chunk = lineChunks[i];
            
            // 🆕 個別チャンクもクリーニング
            var cleanedChunkText = _textCleaner.CleanTextAdvanced(
                chunk.CombinedText, chunk.DetectedLanguage);
            
            result.Append(cleanedChunkText);
            
            // 既存のスペース挿入ロジック（必要に応じて）
            if (i < lineChunks.Count - 1)
            {
                var nextChunk = lineChunks[i + 1];
                var gap = nextChunk.CombinedBounds.X - chunk.CombinedBounds.Right;
                var avgCharWidth = CalculateAverageCharacterWidth(chunk, nextChunk);
                
                if (gap > avgCharWidth * _settings.SpaceInsertionThreshold)
                {
                    result.Append(' ');
                    _logger.LogTrace("スペース挿入: チャンク間隔 {Gap}px > 閾値 {Threshold}px", 
                        gap, avgCharWidth * _settings.SpaceInsertionThreshold);
                }
            }
        }
        
        return result.ToString();
    }
    
    // ... 他の既存メソッドは変更なし
}
```

---

## 🟢 **中優先：言語特化処理（拡張可能設計）**

### **💡 目的・効果**
- ユーザー設定の翻訳先言語に基づく処理分岐
- 将来の言語拡張に対応したプラグイン形式アーキテクチャ

### **🏗️ アーキテクチャ設計**

#### **実装場所**
```
Baketa.Core/Abstractions/Translation/Language/
```

### **💻 拡張可能な言語処理設計**

#### **言語ハンドラー基底クラス**

```csharp
namespace Baketa.Core.Abstractions.Translation.Language;

/// <summary>
/// 言語特化処理の基底クラス
/// 新しい言語サポートは、このクラスを継承して実装
/// </summary>
public abstract class LanguageProcessorBase
{
    public abstract string LanguageCode { get; }
    public abstract string DisplayName { get; }
    
    /// <summary>
    /// 言語特化のテキスト結合
    /// </summary>
    public abstract string CombineTextChunks(IReadOnlyList<TextChunk> chunks);
    
    /// <summary>
    /// 言語特化のテキスト前処理
    /// </summary>
    public virtual string PreprocessText(string text) => text;
    
    /// <summary>
    /// 言語特化の後処理
    /// </summary>
    public virtual string PostprocessText(string text) => text;
    
    /// <summary>
    /// 言語固有の文字種判定
    /// </summary>
    public virtual bool IsNativeScript(char character) => true;
}

/// <summary>
/// 日本語処理ハンドラー
/// </summary>
public sealed class JapaneseLanguageProcessor : LanguageProcessorBase
{
    public override string LanguageCode => "ja";
    public override string DisplayName => "日本語";
    
    public override string CombineTextChunks(IReadOnlyList<TextChunk> chunks)
    {
        // 日本語：直接結合（スペースなし）
        return string.Join("", chunks.Select(c => c.CombinedText));
    }
    
    public override string PreprocessText(string text)
    {
        // 日本語特有の前処理
        text = text.Replace(" ", ""); // 不要なスペース除去
        text = Regex.Replace(text, @"[!！]", "！"); // 感嘆符統一
        text = Regex.Replace(text, @"[?？]", "？"); // 疑問符統一
        return text;
    }
    
    public override bool IsNativeScript(char character)
    {
        // 日本語固有文字（ひらがな・カタカナ・漢字・句読点）
        return (character >= 0x3040 && character <= 0x309F) || // ひらがな
               (character >= 0x30A0 && character <= 0x30FF) || // カタカナ
               (character >= 0x4E00 && character <= 0x9FAF) || // 漢字
               "。、！？".Contains(character);
    }
}

/// <summary>
/// 英語処理ハンドラー
/// </summary>
public sealed class EnglishLanguageProcessor : LanguageProcessorBase
{
    public override string LanguageCode => "en";
    public override string DisplayName => "English";
    
    public override string CombineTextChunks(IReadOnlyList<TextChunk> chunks)
    {
        // 英語：スペース区切りで結合
        return string.Join(" ", chunks.Select(c => c.CombinedText));
    }
    
    public override string PreprocessText(string text)
    {
        // 英語特有の前処理
        text = Regex.Replace(text, @"\s+", " "); // 連続スペース正規化
        text = text.Trim();
        return text;
    }
    
    public override bool IsNativeScript(char character)
    {
        // 英語固有文字（アルファベット・基本句読点）
        return (character >= 'A' && character <= 'Z') ||
               (character >= 'a' && character <= 'z') ||
               ".,!?;:'\"".Contains(character);
    }
}

/// <summary>
/// 中国語処理ハンドラー（将来拡張用）
/// </summary>
public sealed class ChineseLanguageProcessor : LanguageProcessorBase
{
    private readonly ChineseVariant _variant;
    
    public ChineseLanguageProcessor(ChineseVariant variant = ChineseVariant.Simplified)
    {
        _variant = variant;
    }
    
    public override string LanguageCode => _variant == ChineseVariant.Simplified ? "zh-cn" : "zh-tw";
    public override string DisplayName => _variant == ChineseVariant.Simplified ? "简体中文" : "繁體中文";
    
    public override string CombineTextChunks(IReadOnlyList<TextChunk> chunks)
    {
        // 中国語：直接結合（日本語と同様）
        return string.Join("", chunks.Select(c => c.CombinedText));
    }
}

public enum ChineseVariant
{
    Simplified,  // 简体
    Traditional // 繁体
}

/// <summary>
/// デフォルト言語処理ハンドラー（フォールバック用）
/// </summary>
public sealed class DefaultLanguageProcessor : LanguageProcessorBase
{
    public override string LanguageCode => "default";
    public override string DisplayName => "Default";
    
    public override string CombineTextChunks(IReadOnlyList<TextChunk> chunks)
    {
        // デフォルト：スペース区切りで結合
        return string.Join(" ", chunks.Select(c => c.CombinedText));
    }
}
```

#### **言語プロセッサーファクトリー**

```csharp
/// <summary>
/// 言語プロセッサーのファクトリーインターface
/// </summary>
public interface ILanguageProcessorFactory
{
    LanguageProcessorBase GetProcessor(string languageCode);
    IReadOnlyList<LanguageProcessorBase> GetAllProcessors();
    bool IsLanguageSupported(string languageCode);
}

/// <summary>
/// 言語プロセッサーファクトリーの実装
/// 新しい言語は、コンストラクタで追加するだけで拡張可能
/// </summary>
public sealed class LanguageProcessorFactory : ILanguageProcessorFactory
{
    private readonly Dictionary<string, LanguageProcessorBase> _processors;
    private readonly DefaultLanguageProcessor _defaultProcessor;
    
    public LanguageProcessorFactory()
    {
        _defaultProcessor = new DefaultLanguageProcessor();
        
        _processors = new Dictionary<string, LanguageProcessorBase>(StringComparer.OrdinalIgnoreCase)
        {
            { "ja", new JapaneseLanguageProcessor() },
            { "en", new EnglishLanguageProcessor() },
            { "zh-cn", new ChineseLanguageProcessor(ChineseVariant.Simplified) },
            { "zh-tw", new ChineseLanguageProcessor(ChineseVariant.Traditional) },
            
            // 📝 新言語はここに追加するだけで拡張可能
            // { "ko", new KoreanLanguageProcessor() },      // 韓国語（将来追加）
            // { "fr", new FrenchLanguageProcessor() },      // フランス語（将来追加）
            // { "de", new GermanLanguageProcessor() },      // ドイツ語（将来追加）
        };
    }
    
    public LanguageProcessorBase GetProcessor(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
            return _defaultProcessor;
        
        return _processors.TryGetValue(languageCode, out var processor) 
            ? processor 
            : _defaultProcessor;
    }
    
    public IReadOnlyList<LanguageProcessorBase> GetAllProcessors()
    {
        return _processors.Values.ToList();
    }
    
    public bool IsLanguageSupported(string languageCode)
    {
        return !string.IsNullOrWhiteSpace(languageCode) && 
               _processors.ContainsKey(languageCode);
    }
}
```

#### **ユーザー設定システムとの統合**

```csharp
/// <summary>
/// ユーザー設定を考慮した言語認識テキスト処理
/// </summary>
public sealed class LanguageAwareTextProcessor
{
    private readonly ILanguageProcessorFactory _processorFactory;
    private readonly LanguagePairSelectionViewModel _languageSettings;
    private readonly ILogger<LanguageAwareTextProcessor> _logger;
    
    public LanguageAwareTextProcessor(
        ILanguageProcessorFactory processorFactory,
        LanguagePairSelectionViewModel languageSettings,
        ILogger<LanguageAwareTextProcessor> logger)
    {
        _processorFactory = processorFactory;
        _languageSettings = languageSettings;
        _logger = logger;
    }
    
    /// <summary>
    /// ユーザー設定言語に基づく処理
    /// </summary>
    public string ProcessTextChunks(IReadOnlyList<TextChunk> chunks)
    {
        if (chunks.Count == 0) return string.Empty;
        
        // ユーザーの翻訳先言語設定を取得
        var targetLanguage = ExtractTargetLanguageFromUserSettings();
        
        // 対応する言語プロセッサーを取得
        var processor = _processorFactory.GetProcessor(targetLanguage);
        
        _logger.LogDebug("言語特化処理実行: {TargetLanguage} ({ProcessorType})", 
            targetLanguage, processor.GetType().Name);
        
        // 言語特化処理を実行
        var combinedText = processor.CombineTextChunks(chunks);
        return processor.PreprocessText(combinedText);
    }
    
    /// <summary>
    /// ユーザー設定から翻訳先言語を抽出
    /// </summary>
    private string ExtractTargetLanguageFromUserSettings()
    {
        var languagePair = _languageSettings.SelectedLanguagePair?.LanguagePairKey ?? "ja-en";
        
        // "ja-en" → "en" (翻訳先言語)
        // "en-ja" → "ja" (翻訳先言語)
        var parts = languagePair.Split('-');
        if (parts.Length >= 2)
        {
            return parts[1]; // 翻訳先言語
        }
        
        // フォールバック
        return "en";
    }
    
    /// <summary>
    /// 翻訳結果の後処理
    /// </summary>
    public string PostprocessTranslationResult(string translatedText, string targetLanguage)
    {
        var processor = _processorFactory.GetProcessor(targetLanguage);
        return processor.PostprocessText(translatedText);
    }
}
```

---

## 📅 **実装スケジュール**

### **Week 1: TimedChunkAggregator 実装**
- **Day 1-2**: コア実装 (`TimedChunkAggregator`, `TimedAggregatorSettings`)
- **Day 3-4**: `BatchOcrIntegrationService`統合
- **Day 5**: 統合テスト・デバッグ

### **Week 2 前半: 強化ノイズ除去**
- **Day 1-2**: `AdvancedTextCleaner`実装
- **Day 3**: `CoordinateBasedLineBreakProcessor`統合・テスト

### **Week 2 後半: 言語特化処理**
- **Day 4-5**: 言語プロセッサーアーキテクチャ実装

### **Week 3: 統合テスト・最適化**
- 全機能統合テスト
- パフォーマンス最適化
- ユーザーテスト

---

## 🔧 **DI登録・設定**

### **サービス登録例**

```csharp
// DIコンテナ登録
public static class TranslationQualityServiceExtensions
{
    public static IServiceCollection AddTranslationQualityImprovement(
        this IServiceCollection services)
    {
        // 環境別設定を使用（Feature Flagで段階的導入）
        services.AddSingleton(
#if DEBUG
            TimedAggregatorSettings.Development
#else
            TimedAggregatorSettings.Production
#endif
        );
        
        services.AddSingleton(AdvancedCleaningSettings.Default);
        
        // 核心サービス
        services.AddSingleton<TimedChunkAggregator>();
        services.AddSingleton<AdvancedTextCleaner>();
        services.AddSingleton<ILanguageProcessorFactory, LanguageProcessorFactory>();
        services.AddTransient<LanguageAwareTextProcessor>();
        
        // 統合サービス（既存のBatchOcrIntegrationServiceを置き換え）
        services.AddSingleton<EnhancedBatchOcrIntegrationService>();
        
        return services;
    }
}

// Program.cs または Startup.cs
services.AddTranslationQualityImprovement();
```

### **設定ファイル例 (appsettings.json)**

```json
{
  "TranslationQuality": {
    "TimedAggregator": {
      "BufferDelayMs": 150,
      "MaxChunkCount": 50,
      "ForceFlushMs": 1000
    },
    "AdvancedCleaning": {
      "EnableVerboseLogging": false,
      "EnableLanguageSpecificCleaning": true,
      "EnableMisrecognitionCorrection": true
    }
  }
}
```

---

## 📊 **期待される成果**

### **定量的改善目標**

| 項目 | 改善前 | 改善後 | 向上率 |
|------|--------|--------|--------|
| **翻訳品質** | 個別チャンク翻訳 | 文脈統合翻訳 | **40-60%向上** |
| **OCR精度** | ノイズ付きテキスト | クリーン化テキスト | **20-30%向上** |
| **多言語対応** | 汎用処理のみ | 言語特化処理 | **自然性大幅向上** |
| **体感速度** | リアルタイム | 150ms遅延 | **知覚差なし** |
| **メモリ使用量** | チャンク蓄積なし | 制御された蓄積 | **適切な制御** |

### **定性的改善効果**

1. **ユーザー体験の向上**
   - より自然で読みやすい翻訳結果
   - 文脈を考慮した一貫性のある翻訳
   - 言語固有の表現に配慮した処理

2. **システムの拡張性向上**
   - 新言語追加が容易なプラグイン形式
   - 設定変更による調整可能性
   - 将来の機能拡張への対応力

3. **保守性の向上**
   - 責任分離の明確化
   - テストしやすいアーキテクチャ
   - ログによる追跡可能性

**総合的に、翻訳システム全体で60-80%の品質向上が期待されます。**

---

## 🚨 **リスク管理と対策**

### **技術リスク**

1. **メモリリーク**: TimedChunkAggregatorでのチャンク蓄積
   - **対策**: MaxChunkCount制限、定期的な強制フラッシュ
   
2. **パフォーマンス**: 150msの遅延による体感速度低下
   - **対策**: ユーザー設定可能、段階的調整機能

3. **統合複雑性**: 既存システムとの統合時の不具合
   - **対策**: 段階的ロールアウト、フィーチャーフラグ

### **運用リスク**

1. **設定ミス**: 不適切な遅延時間設定
   - **対策**: デフォルト値の慎重な選択、設定UI整備
   
2. **言語判定ミス**: 誤った言語プロセッサー選択
   - **対策**: フォールバック機能、ログによる追跡

---

## 📚 **関連ドキュメント**

- [既存翻訳システムアーキテクチャ](./translation-interfaces.md)
- [OCRシステム実装仕様](../ocr-system/ocr-implementation.md)
- [ReactiveUI設定システム](../ui-system/reactiveui-guide.md)
- [イベント集約システム](../event-system/event-system-overview.md)

---

## 🔄 **更新履歴**

- **v1.0** (2025-09-01): 初版作成 - 翻訳精度向上戦略の策定
- **v1.1** (2025-09-01): Geminiフィードバック反映完了
  - SourceWindowHandle別バッファ管理によるコンテキスト分離
  - ForceFlushMs制御による無限タイマーリセット防止
  - async void メソッドでの包括的エラーハンドリング
  - Interlocked.Increment使用による thread-safe ChunkID生成
  - コンパイル済みRegex使用によるパフォーマンス最適化

---

**このドキュメントは、Baketaの翻訳品質を次のレベルに押し上げるための包括的な実装戦略を提供します。段階的な実装により、リスクを最小限に抑えながら大幅な品質向上を実現できます。**