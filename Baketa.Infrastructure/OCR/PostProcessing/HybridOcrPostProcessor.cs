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
public class HybridOcrPostProcessor : IOcrPostProcessor
{
    private readonly ILogger<HybridOcrPostProcessor> _logger;
    private readonly JapaneseOcrPostProcessor _dictionaryProcessor;
    private readonly NgramOcrPostProcessor _ngramProcessor;
    private readonly bool _useNgramFirst;
    
    public HybridOcrPostProcessor(
        ILogger<HybridOcrPostProcessor> logger,
        JapaneseOcrPostProcessor dictionaryProcessor,
        NgramOcrPostProcessor ngramProcessor,
        bool useNgramFirst = true)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dictionaryProcessor = dictionaryProcessor ?? throw new ArgumentNullException(nameof(dictionaryProcessor));
        _ngramProcessor = ngramProcessor ?? throw new ArgumentNullException(nameof(ngramProcessor));
        _useNgramFirst = useNgramFirst;
    }
    
    /// <summary>
    /// ハイブリッド後処理を実行
    /// </summary>
    public async Task<string> ProcessAsync(string rawText, float confidence)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return rawText;
        
        _logger.LogDebug("ハイブリッドOCR後処理開始: {Text} (信頼度: {Confidence})", rawText, confidence);
        
        string processedText;
        
        if (_useNgramFirst)
        {
            // N-gramベース処理を最初に実行
            var ngramResult = await _ngramProcessor.ProcessAsync(rawText, confidence);
            
            // 辞書ベース処理で仕上げ
            processedText = await _dictionaryProcessor.ProcessAsync(ngramResult, confidence);
        }
        else
        {
            // 辞書ベース処理を最初に実行
            var dictionaryResult = await _dictionaryProcessor.ProcessAsync(rawText, confidence);
            
            // N-gramベース処理で仕上げ
            processedText = await _ngramProcessor.ProcessAsync(dictionaryResult, confidence);
        }
        
        _logger.LogDebug("ハイブリッドOCR後処理完了: {OriginalText} -> {ProcessedText}", rawText, processedText);
        
        return processedText;
    }
    
    /// <summary>
    /// ハイブリッド後処理を実行（信頼度なし）
    /// </summary>
    public async Task<string> ProcessAsync(string ocrText)
    {
        return await ProcessAsync(ocrText, 1.0f);
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
        return await Task.WhenAll(tasks);
    }
    
    /// <summary>
    /// 異なる処理順序での結果を比較
    /// </summary>
    public async Task<ProcessingComparisonResult> CompareProcessingOrdersAsync(string ocrText)
    {
        _logger.LogDebug("処理順序比較開始: {Text}", ocrText);
        
        // N-gram → Dictionary
        var ngramResult = await _ngramProcessor.ProcessAsync(ocrText);
        var ngramFirstResult = await _dictionaryProcessor.ProcessAsync(ngramResult, 1.0f);
        
        // Dictionary → N-gram
        var dictionaryResult = await _dictionaryProcessor.ProcessAsync(ocrText, 1.0f);
        var dictionaryFirstResult = await _ngramProcessor.ProcessAsync(dictionaryResult);
        
        // N-gramのみ
        var ngramOnlyResult = await _ngramProcessor.ProcessAsync(ocrText);
        
        // Dictionaryのみ
        var dictionaryOnlyResult = await _dictionaryProcessor.ProcessAsync(ocrText, 1.0f);
        
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
public class ProcessingComparisonResult
{
    public string OriginalText { get; }
    public string NgramFirstResult { get; }
    public string DictionaryFirstResult { get; }
    public string NgramOnlyResult { get; }
    public string DictionaryOnlyResult { get; }
    
    public ProcessingComparisonResult(
        string originalText,
        string ngramFirstResult,
        string dictionaryFirstResult,
        string ngramOnlyResult,
        string dictionaryOnlyResult)
    {
        OriginalText = originalText;
        NgramFirstResult = ngramFirstResult;
        DictionaryFirstResult = dictionaryFirstResult;
        NgramOnlyResult = ngramOnlyResult;
        DictionaryOnlyResult = dictionaryOnlyResult;
    }
    
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
        return new[] { NgramFirstResult, DictionaryFirstResult, NgramOnlyResult, DictionaryOnlyResult }
            .Distinct()
            .ToList();
    }
}

/// <summary>
/// ハイブリッド後処理のファクトリクラス
/// </summary>
public class HybridOcrPostProcessorFactory
{
    private readonly ILogger<HybridOcrPostProcessorFactory> _logger;
    private readonly NgramTrainingService _trainingService;
    
    public HybridOcrPostProcessorFactory(
        ILogger<HybridOcrPostProcessorFactory> logger,
        NgramTrainingService trainingService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _trainingService = trainingService ?? throw new ArgumentNullException(nameof(trainingService));
    }
    
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
        var ngramModel = await _trainingService.LoadJapaneseBigramModelAsync();
        
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
        var ngramModel = await _trainingService.RetrainWithCustomDataAsync(customTexts);
        
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