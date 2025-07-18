using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Baketa.Infrastructure.OCR.PostProcessing.NgramModels;

namespace Baketa.Infrastructure.OCR.PostProcessing;

/// <summary>
/// 辞書ベースとN-gramベースの後処理を統合したハイブリッドプロセッサ
/// </summary>
public sealed class HybridOcrPostProcessor(
    ILogger<HybridOcrPostProcessor> logger,
    JapaneseOcrPostProcessor dictionaryProcessor,
    NgramOcrPostProcessor ngramProcessor,
    bool useNgramFirst = true) : IOcrPostProcessor
{
    private readonly ILogger<HybridOcrPostProcessor> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly JapaneseOcrPostProcessor _dictionaryProcessor = dictionaryProcessor ?? throw new ArgumentNullException(nameof(dictionaryProcessor));
    private readonly NgramOcrPostProcessor _ngramProcessor = ngramProcessor ?? throw new ArgumentNullException(nameof(ngramProcessor));

    /// <summary>
    /// ハイブリッド後処理を実行
    /// </summary>
    public async Task<string> ProcessAsync(string rawText, float confidence)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return rawText;
        
        _logger.LogDebug("ハイブリッドOCR後処理開始: {Text} (信頼度: {Confidence})", rawText, confidence);
        
        string processedText;
        
        if (useNgramFirst)
        {
            // N-gramベース処理を最初に実行
            var ngramResult = await _ngramProcessor.ProcessAsync(rawText, confidence).ConfigureAwait(false);
            
            // 辞書ベース処理で仕上げ
            processedText = await _dictionaryProcessor.ProcessAsync(ngramResult, confidence).ConfigureAwait(false);
        }
        else
        {
            // 辞書ベース処理を最初に実行
            var dictionaryResult = await _dictionaryProcessor.ProcessAsync(rawText, confidence).ConfigureAwait(false);
            
            // N-gramベース処理で仕上げ
            processedText = await _ngramProcessor.ProcessAsync(dictionaryResult, confidence).ConfigureAwait(false);
        }
        
        _logger.LogDebug("ハイブリッドOCR後処理完了: {OriginalText} -> {ProcessedText}", rawText, processedText);
        
        return processedText;
    }
    
    /// <summary>
    /// ハイブリッド後処理を実行（信頼度なし）
    /// </summary>
    public async Task<string> ProcessAsync(string ocrText)
    {
        return await ProcessAsync(ocrText, 1.0f).ConfigureAwait(false);
    }
    
    /// <summary>
    /// よくある誤認識パターンを修正
    /// </summary>
    public string CorrectCommonErrors(string text)
    {
        return _dictionaryProcessor.CorrectCommonErrors(text);
    }
    
    /// <summary>
    /// 後処理統計を取得
    /// </summary>
    public PostProcessingStats GetStats()
    {
        var dictionaryStats = _dictionaryProcessor.GetStats();
        var ngramStats = _ngramProcessor.GetStats();
        
        return new PostProcessingStats
        {
            TotalProcessed = dictionaryStats.TotalProcessed + ngramStats.TotalProcessed,
            CorrectionsApplied = dictionaryStats.CorrectionsApplied + ngramStats.CorrectionsApplied,
            TopCorrectionPatterns = dictionaryStats.TopCorrectionPatterns
                .Concat(ngramStats.TopCorrectionPatterns)
                .GroupBy(kvp => kvp.Key)
                .ToDictionary(g => g.Key, g => g.Sum(kvp => kvp.Value))
        };
    }
    
    /// <summary>
    /// 複数のOCR結果テキストを並行処理
    /// </summary>
    public async Task<IEnumerable<string>> ProcessBatchAsync(IEnumerable<string> ocrTexts)
    {
        var tasks = ocrTexts.Select(text => ProcessAsync(text));
        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }
    
    /// <summary>
    /// 異なる処理順序での結果を比較
    /// </summary>
    public async Task<ProcessingComparisonResult> CompareProcessingOrdersAsync(string ocrText)
    {
        _logger.LogDebug("処理順序比較開始: {Text}", ocrText);
        
        // N-gram → Dictionary
        var ngramResult = await _ngramProcessor.ProcessAsync(ocrText).ConfigureAwait(false);
        var ngramFirstResult = await _dictionaryProcessor.ProcessAsync(ngramResult, 1.0f).ConfigureAwait(false);
        
        // Dictionary → N-gram
        var dictionaryResult = await _dictionaryProcessor.ProcessAsync(ocrText, 1.0f).ConfigureAwait(false);
        var dictionaryFirstResult = await _ngramProcessor.ProcessAsync(dictionaryResult).ConfigureAwait(false);
        
        // N-gramのみ
        var ngramOnlyResult = await _ngramProcessor.ProcessAsync(ocrText).ConfigureAwait(false);
        
        // Dictionaryのみ
        var dictionaryOnlyResult = await _dictionaryProcessor.ProcessAsync(ocrText, 1.0f).ConfigureAwait(false);
        
        return new ProcessingComparisonResult(
            ocrText,
            ngramFirstResult,
            dictionaryFirstResult,
            ngramOnlyResult,
            dictionaryOnlyResult);
    }
}

/// <summary>
/// 処理順序比較結果
/// </summary>
public sealed class ProcessingComparisonResult(
    string originalText,
    string ngramFirstResult,
    string dictionaryFirstResult,
    string ngramOnlyResult,
    string dictionaryOnlyResult)
{
    public string OriginalText { get; } = originalText;
    public string NgramFirstResult { get; } = ngramFirstResult;
    public string DictionaryFirstResult { get; } = dictionaryFirstResult;
    public string NgramOnlyResult { get; } = ngramOnlyResult;
    public string DictionaryOnlyResult { get; } = dictionaryOnlyResult;

    /// <summary>
    /// 全ての結果が同じかチェック
    /// </summary>
    public bool AllResultsMatch => 
        NgramFirstResult == DictionaryFirstResult &&
        NgramFirstResult == NgramOnlyResult &&
        NgramFirstResult == DictionaryOnlyResult;
    
    /// <summary>
    /// 結果の一意性を取得
    /// </summary>
    public IEnumerable<string> GetUniqueResults()
    {
        return [.. new[] { NgramFirstResult, DictionaryFirstResult, NgramOnlyResult, DictionaryOnlyResult }.Distinct()];
    }
}

/// <summary>
/// ハイブリッド後処理のファクトリクラス
/// </summary>
public sealed class HybridOcrPostProcessorFactory(
    ILogger<HybridOcrPostProcessorFactory> logger,
    NgramTrainingService trainingService)
{
    private readonly ILogger<HybridOcrPostProcessorFactory> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly NgramTrainingService _trainingService = trainingService ?? throw new ArgumentNullException(nameof(trainingService));

    /// <summary>
    /// ハイブリッドプロセッサを作成
    /// </summary>
    public async Task<HybridOcrPostProcessor> CreateAsync()
    {
        _logger.LogInformation("ハイブリッドOCR後処理プロセッサを作成中");
        
        // 辞書ベースプロセッサ
        var dictionaryProcessor = new JapaneseOcrPostProcessor(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<JapaneseOcrPostProcessor>.Instance);
        
        // N-gramモデルの読み込み
        var ngramModel = await _trainingService.LoadJapaneseBigramModelAsync().ConfigureAwait(false);
        
        // N-gramベースプロセッサ
        var ngramProcessor = new NgramOcrPostProcessor(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<NgramOcrPostProcessor>.Instance, 
            ngramModel);
        
        // ハイブリッドプロセッサの作成
        var hybridProcessor = new HybridOcrPostProcessor(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<HybridOcrPostProcessor>.Instance,
            dictionaryProcessor,
            ngramProcessor,
            useNgramFirst: true);
        
        _logger.LogInformation("ハイブリッドOCR後処理プロセッサ作成完了");
        
        return hybridProcessor;
    }
    
    /// <summary>
    /// カスタムデータでN-gramモデルを訓練してプロセッサを作成
    /// </summary>
    public async Task<HybridOcrPostProcessor> CreateWithCustomTrainingAsync(IEnumerable<string> customTexts)
    {
        _logger.LogInformation("カスタムデータでハイブリッドOCR後処理プロセッサを作成中");
        
        // 辞書ベースプロセッサ
        var dictionaryProcessor = new JapaneseOcrPostProcessor(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<JapaneseOcrPostProcessor>.Instance);
        
        // カスタムデータでN-gramモデルを訓練
        var ngramModel = await _trainingService.RetrainWithCustomDataAsync(customTexts).ConfigureAwait(false);
        
        // N-gramベースプロセッサ
        var ngramProcessor = new NgramOcrPostProcessor(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<NgramOcrPostProcessor>.Instance, 
            ngramModel);
        
        // ハイブリッドプロセッサの作成
        var hybridProcessor = new HybridOcrPostProcessor(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<HybridOcrPostProcessor>.Instance,
            dictionaryProcessor,
            ngramProcessor,
            useNgramFirst: true);
        
        _logger.LogInformation("カスタムハイブリッドOCR後処理プロセッサ作成完了");
        
        return hybridProcessor;
    }
}
