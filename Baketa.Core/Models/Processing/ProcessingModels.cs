using Baketa.Core.Abstractions.Imaging;
using System.Drawing;

namespace Baketa.Core.Models.Processing;

/// <summary>
/// 処理段階種別
/// </summary>
public enum ProcessingStageType
{
    /// <summary>
    /// 画像変化検知段階
    /// </summary>
    ImageChangeDetection = 1,
    
    /// <summary>
    /// OCR実行段階
    /// </summary>
    OcrExecution = 2,
    
    /// <summary>
    /// テキスト変化検知段階
    /// </summary>
    TextChangeDetection = 3,
    
    /// <summary>
    /// 翻訳実行段階
    /// </summary>
    TranslationExecution = 4
}

/// <summary>
/// パイプライン処理の入力情報
/// Geminiフィードバック反映: Record型でイミュータブル設計
/// </summary>
public sealed record ProcessingPipelineInput
{
    /// <summary>
    /// キャプチャされた画像
    /// </summary>
    public required IImage CapturedImage { get; init; }
    
    /// <summary>
    /// キャプチャ領域
    /// </summary>
    public required Rectangle CaptureRegion { get; init; }
    
    /// <summary>
    /// ソースウィンドウハンドル
    /// </summary>
    public required IntPtr SourceWindowHandle { get; init; }
    
    /// <summary>
    /// キャプチャタイムスタンプ
    /// </summary>
    public DateTime CaptureTimestamp { get; init; } = DateTime.UtcNow;
    
    /// <summary>
    /// 処理オプション
    /// </summary>
    public ProcessingPipelineOptions Options { get; init; } = new();
    
    /// <summary>
    /// 前回の画像ハッシュ（画像変化検知用）
    /// </summary>
    public string? PreviousImageHash { get; init; }
    
    /// <summary>
    /// 前回のOCRテキスト（テキスト変化検知用）
    /// </summary>
    public string? PreviousOcrText { get; init; }
    
    /// <summary>
    /// 処理コンテキストID（スレッドセーフ管理用）
    /// </summary>
    public string ContextId => $"Window_{SourceWindowHandle.ToInt64()}";
}

/// <summary>
/// パイプライン処理オプション
/// </summary>
public sealed record ProcessingPipelineOptions
{
    /// <summary>
    /// 段階的処理を有効にするか
    /// </summary>
    public bool EnableStaging { get; init; } = true;
    
    /// <summary>
    /// パフォーマンスメトリクスを収集するか
    /// </summary>
    public bool EnablePerformanceMetrics { get; init; } = true;
    
    /// <summary>
    /// 早期終了を有効にするか
    /// </summary>
    public bool EnableEarlyTermination { get; init; } = true;
    
    /// <summary>
    /// 強制的に全段階を実行するか（デバッグ用）
    /// </summary>
    public bool ForceCompleteExecution { get; init; } = false;
}

/// <summary>
/// パイプライン処理結果
/// Geminiフィードバック反映: Record型でイミュータブル設計
/// </summary>
public sealed record ProcessingPipelineResult
{
    /// <summary>
    /// 処理継続すべきか
    /// </summary>
    public required bool ShouldContinue { get; init; }
    
    /// <summary>
    /// 最後に完了した段階
    /// </summary>
    public required ProcessingStageType LastCompletedStage { get; init; }
    
    /// <summary>
    /// 総処理時間
    /// </summary>
    public required TimeSpan TotalElapsedTime { get; init; }
    
    /// <summary>
    /// 処理成功フラグ
    /// </summary>
    public required bool Success { get; init; }
    
    /// <summary>
    /// OCR結果テキスト
    /// </summary>
    public string? OcrResultText { get; init; }
    
    /// <summary>
    /// 翻訳結果テキスト
    /// </summary>
    public string? TranslationResultText { get; init; }
    
    /// <summary>
    /// 画像変化検知結果
    /// </summary>
    public ImageChangeDetectionResult? ImageChangeResult { get; init; }
    
    /// <summary>
    /// OCR実行結果
    /// </summary>
    public OcrExecutionResult? OcrResult { get; init; }
    
    /// <summary>
    /// テキスト変化検知結果
    /// </summary>
    public TextChangeDetectionResult? TextChangeResult { get; init; }
    
    /// <summary>
    /// 翻訳実行結果
    /// </summary>
    public TranslationExecutionResult? TranslationResult { get; init; }
    
    /// <summary>
    /// 処理エラー
    /// </summary>
    public Exception? Error { get; init; }
    
    /// <summary>
    /// エラーメッセージ
    /// </summary>
    public string? ErrorMessage { get; init; }
    
    /// <summary>
    /// パフォーマンスメトリクス
    /// </summary>
    public ProcessingMetrics Metrics { get; init; } = new();
    
    /// <summary>
    /// 実行された段階リスト
    /// </summary>
    public IReadOnlyList<ProcessingStageType> ExecutedStages { get; init; } = Array.Empty<ProcessingStageType>();
    
    /// <summary>
    /// 段階別処理時間
    /// </summary>
    public IReadOnlyDictionary<ProcessingStageType, TimeSpan> StageProcessingTimes { get; init; } = 
        new Dictionary<ProcessingStageType, TimeSpan>();
    
    /// <summary>
    /// エラー結果を作成
    /// </summary>
    public static ProcessingPipelineResult CreateError(string errorMessage, TimeSpan totalTime, Exception? error = null, ProcessingStageType lastStage = ProcessingStageType.ImageChangeDetection)
    {
        return new ProcessingPipelineResult
        {
            ShouldContinue = false,
            LastCompletedStage = lastStage,
            TotalElapsedTime = totalTime,
            Success = false,
            ErrorMessage = errorMessage,
            Error = error
        };
    }
    
    /// <summary>
    /// 成功結果を作成
    /// </summary>
    public static ProcessingPipelineResult CreateSuccess(
        ProcessingStageType lastStage, 
        TimeSpan totalTime,
        IReadOnlyList<ProcessingStageType> executedStages,
        IReadOnlyDictionary<ProcessingStageType, TimeSpan> stageTimes)
    {
        return new ProcessingPipelineResult
        {
            ShouldContinue = true,
            LastCompletedStage = lastStage,
            TotalElapsedTime = totalTime,
            Success = true,
            ExecutedStages = executedStages,
            StageProcessingTimes = stageTimes
        };
    }
}

/// <summary>
/// 段階別処理結果
/// </summary>
public sealed record ProcessingStageResult
{
    /// <summary>
    /// 処理成功フラグ
    /// </summary>
    public required bool Success { get; init; }
    
    /// <summary>
    /// 段階種別
    /// </summary>
    public required ProcessingStageType StageType { get; init; }
    
    /// <summary>
    /// 処理時間
    /// </summary>
    public required TimeSpan ProcessingTime { get; init; }
    
    /// <summary>
    /// 段階結果データ
    /// </summary>
    public object? Data { get; init; }
    
    /// <summary>
    /// エラーメッセージ
    /// </summary>
    public string? ErrorMessage { get; init; }
    
    /// <summary>
    /// 処理がスキップされたかどうか
    /// </summary>
    public bool Skipped { get; init; }
    
    /// <summary>
    /// 成功結果を作成
    /// </summary>
    public static ProcessingStageResult CreateSuccess(ProcessingStageType stageType, object data, TimeSpan processingTime = default)
    {
        return new ProcessingStageResult
        {
            Success = true,
            StageType = stageType,
            ProcessingTime = processingTime,
            Data = data
        };
    }
    
    /// <summary>
    /// エラー結果を作成
    /// </summary>
    public static ProcessingStageResult CreateError(ProcessingStageType stageType, string errorMessage, TimeSpan processingTime = default)
    {
        return new ProcessingStageResult
        {
            Success = false,
            StageType = stageType,
            ProcessingTime = processingTime,
            ErrorMessage = errorMessage
        };
    }
    
    /// <summary>
    /// スキップ結果を作成
    /// </summary>
    public static ProcessingStageResult CreateSkipped(ProcessingStageType stageType, string reason)
    {
        return new ProcessingStageResult
        {
            Success = true,
            StageType = stageType,
            ProcessingTime = TimeSpan.Zero,
            Skipped = true,
            ErrorMessage = reason
        };
    }
}

/// <summary>
/// 処理コンテキスト
/// 段階間でのデータ共有とステート管理
/// </summary>
public sealed class ProcessingContext
{
    private readonly Dictionary<ProcessingStageType, ProcessingStageResult> _stageResults = [];
    
    /// <summary>
    /// 処理入力データ
    /// </summary>
    public ProcessingPipelineInput Input { get; }
    
    /// <summary>
    /// 直前段階の処理結果
    /// </summary>
    public ProcessingStageResult? PreviousStageResult { get; private set; }
    
    /// <summary>
    /// コンストラクタ
    /// </summary>
    public ProcessingContext(ProcessingPipelineInput input)
    {
        Input = input ?? throw new ArgumentNullException(nameof(input));
    }
    
    /// <summary>
    /// 段階結果を追加
    /// </summary>
    public void AddStageResult(ProcessingStageType stageType, ProcessingStageResult result)
    {
        _stageResults[stageType] = result;
        PreviousStageResult = result;
    }
    
    /// <summary>
    /// 特定段階の結果を取得
    /// </summary>
    public T? GetStageResult<T>(ProcessingStageType stageType) where T : class
    {
        if (_stageResults.TryGetValue(stageType, out var result))
        {
            return result.Data as T;
        }
        return null;
    }
    
    /// <summary>
    /// 段階結果が存在するかチェック
    /// </summary>
    public bool HasStageResult(ProcessingStageType stageType)
    {
        return _stageResults.ContainsKey(stageType);
    }
    
    /// <summary>
    /// 全段階結果を取得
    /// </summary>
    public IReadOnlyDictionary<ProcessingStageType, ProcessingStageResult> GetAllStageResults()
    {
        return _stageResults;
    }
}

/// <summary>
/// パフォーマンスメトリクス
/// </summary>
public sealed record ProcessingMetrics
{
    /// <summary>
    /// 段階別処理時間
    /// </summary>
    public Dictionary<ProcessingStageType, TimeSpan> StageProcessingTimes { get; init; } = [];
    
    /// <summary>
    /// 総段階数
    /// </summary>
    public int TotalStages { get; init; }
    
    /// <summary>
    /// 実行段階数
    /// </summary>
    public int ExecutedStages { get; init; }
    
    /// <summary>
    /// スキップ段階数
    /// </summary>
    public int SkippedStages { get; init; }
    
    /// <summary>
    /// 早期終了フラグ
    /// </summary>
    public bool EarlyTerminated { get; init; }
    
    /// <summary>
    /// CPU使用率削減効果（推定）
    /// </summary>
    public float EstimatedCpuReduction { get; init; }
}

// 段階別結果データモデル

/// <summary>
/// 画像変化検知結果
/// </summary>
public sealed record ImageChangeDetectionResult
{
    public required bool HasChanged { get; init; }
    public required float ChangePercentage { get; init; }
    public string? PreviousHash { get; init; }
    public string? CurrentHash { get; init; }
    public TimeSpan ProcessingTime { get; init; }
    public string AlgorithmUsed { get; init; } = "DifferenceHash";
    
    public static ImageChangeDetectionResult CreateFirstTime()
    {
        return new ImageChangeDetectionResult
        {
            HasChanged = true,
            ChangePercentage = 1.0f,
            AlgorithmUsed = "FirstTime"
        };
    }
}

/// <summary>
/// OCR実行結果
/// </summary>
public sealed record OcrExecutionResult
{
    public required string DetectedText { get; init; }
    public List<object> TextChunks { get; init; } = [];
    public TimeSpan ProcessingTime { get; init; }
    public bool Success { get; init; } = true;
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// テキスト変化検知結果（Processing用）
/// </summary>
public sealed record TextChangeDetectionResult
{
    public required bool HasTextChanged { get; init; }
    public required float ChangePercentage { get; init; }
    public string? PreviousText { get; init; }
    public string? CurrentText { get; init; }
    public TimeSpan ProcessingTime { get; init; }
    public string AlgorithmUsed { get; init; } = "EditDistance";
    
    public static TextChangeDetectionResult CreateFirstTime(string currentText)
    {
        return new TextChangeDetectionResult
        {
            HasTextChanged = true,
            ChangePercentage = 1.0f,
            CurrentText = currentText,
            AlgorithmUsed = "FirstTime"
        };
    }
}

/// <summary>
/// 翻訳実行結果
/// </summary>
public sealed record TranslationExecutionResult
{
    public required string TranslatedText { get; init; }
    public List<object> TranslatedChunks { get; init; } = [];
    public TimeSpan ProcessingTime { get; init; }
    public bool Success { get; init; } = true;
    public string EngineUsed { get; init; } = "Unknown";
    public string? ErrorMessage { get; init; }
}