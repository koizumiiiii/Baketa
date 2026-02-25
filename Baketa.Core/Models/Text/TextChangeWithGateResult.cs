using Baketa.Core.Abstractions.Text;

namespace Baketa.Core.Models.Text;

/// <summary>
/// [Issue #293] Gate判定を含むテキスト変化検知結果
/// </summary>
/// <remarks>
/// Geminiフィードバック反映: record として定義しアサーションを書きやすく。
/// </remarks>
public sealed record TextChangeWithGateResult
{
    /// <summary>
    /// 変化率 (0.0-1.0)
    /// </summary>
    public required float ChangePercentage { get; init; }

    /// <summary>
    /// 翻訳を実行すべきかどうか
    /// </summary>
    public required bool ShouldTranslate { get; init; }

    /// <summary>
    /// Gate判定理由
    /// </summary>
    public required GateDecision Decision { get; init; }

    /// <summary>
    /// 適用された閾値
    /// </summary>
    public required float AppliedThreshold { get; init; }

    /// <summary>
    /// 前回のテキスト（初回はnull）
    /// </summary>
    public string? PreviousText { get; init; }

    /// <summary>
    /// 現在のテキスト
    /// </summary>
    public string? CurrentText { get; init; }

    /// <summary>
    /// 編集距離
    /// </summary>
    public int EditDistance { get; init; }

    /// <summary>
    /// 処理時間
    /// </summary>
    public TimeSpan ProcessingTime { get; init; }

    /// <summary>
    /// 初回テキスト（翻訳実行）の結果を作成
    /// </summary>
    public static TextChangeWithGateResult CreateFirstText(
        string currentText,
        float appliedThreshold,
        TimeSpan processingTime = default)
    {
        return new TextChangeWithGateResult
        {
            ChangePercentage = 1.0f,
            ShouldTranslate = true,
            Decision = GateDecision.FirstText,
            AppliedThreshold = appliedThreshold,
            PreviousText = null,
            CurrentText = currentText,
            EditDistance = currentText.Length,
            ProcessingTime = processingTime
        };
    }

    /// <summary>
    /// 十分な変化あり（翻訳実行）の結果を作成
    /// </summary>
    public static TextChangeWithGateResult CreateSufficientChange(
        string? previousText,
        string currentText,
        float changePercentage,
        float appliedThreshold,
        int editDistance,
        TimeSpan processingTime = default)
    {
        return new TextChangeWithGateResult
        {
            ChangePercentage = changePercentage,
            ShouldTranslate = true,
            Decision = GateDecision.SufficientChange,
            AppliedThreshold = appliedThreshold,
            PreviousText = previousText,
            CurrentText = currentText,
            EditDistance = editDistance,
            ProcessingTime = processingTime
        };
    }

    /// <summary>
    /// 変化不足（翻訳スキップ）の結果を作成
    /// </summary>
    public static TextChangeWithGateResult CreateInsufficientChange(
        string? previousText,
        string currentText,
        float changePercentage,
        float appliedThreshold,
        int editDistance,
        TimeSpan processingTime = default)
    {
        return new TextChangeWithGateResult
        {
            ChangePercentage = changePercentage,
            ShouldTranslate = false,
            Decision = GateDecision.InsufficientChange,
            AppliedThreshold = appliedThreshold,
            PreviousText = previousText,
            CurrentText = currentText,
            EditDistance = editDistance,
            ProcessingTime = processingTime
        };
    }

    /// <summary>
    /// 空テキスト（翻訳スキップ）の結果を作成
    /// </summary>
    public static TextChangeWithGateResult CreateEmptyText(
        string? previousText,
        float appliedThreshold,
        TimeSpan processingTime = default)
    {
        return new TextChangeWithGateResult
        {
            ChangePercentage = 0.0f,
            ShouldTranslate = false,
            Decision = GateDecision.EmptyText,
            AppliedThreshold = appliedThreshold,
            PreviousText = previousText,
            CurrentText = string.Empty,
            EditDistance = 0,
            ProcessingTime = processingTime
        };
    }

    /// <summary>
    /// 同一テキスト（翻訳スキップ）の結果を作成
    /// </summary>
    public static TextChangeWithGateResult CreateSameText(
        string text,
        float appliedThreshold,
        TimeSpan processingTime = default)
    {
        return new TextChangeWithGateResult
        {
            ChangePercentage = 0.0f,
            ShouldTranslate = false,
            Decision = GateDecision.SameText,
            AppliedThreshold = appliedThreshold,
            PreviousText = text,
            CurrentText = text,
            EditDistance = 0,
            ProcessingTime = processingTime
        };
    }

    /// <summary>
    /// 最小文字数未満（翻訳スキップ）の結果を作成
    /// </summary>
    public static TextChangeWithGateResult CreateTextTooShort(
        string? previousText,
        string currentText,
        float appliedThreshold,
        TimeSpan processingTime = default)
    {
        return new TextChangeWithGateResult
        {
            ChangePercentage = 0.0f,
            ShouldTranslate = false,
            Decision = GateDecision.TextTooShort,
            AppliedThreshold = appliedThreshold,
            PreviousText = previousText,
            CurrentText = currentText,
            EditDistance = 0,
            ProcessingTime = processingTime
        };
    }

    /// <summary>
    /// [Issue #432] タイプライター成長中（翻訳遅延）の結果を作成
    /// </summary>
    public static TextChangeWithGateResult CreateTypewriterGrowing(
        string? previousText,
        string currentText,
        float appliedThreshold,
        TimeSpan processingTime = default)
    {
        return new TextChangeWithGateResult
        {
            ChangePercentage = 0.0f,
            ShouldTranslate = false,
            Decision = GateDecision.TypewriterGrowing,
            AppliedThreshold = appliedThreshold,
            PreviousText = previousText,
            CurrentText = currentText,
            EditDistance = currentText.Length - (previousText?.Length ?? 0),
            ProcessingTime = processingTime
        };
    }

    /// <summary>
    /// [Issue #432] タイプライター最大遅延超過（強制翻訳）の結果を作成
    /// </summary>
    public static TextChangeWithGateResult CreateTypewriterMaxDelayExceeded(
        string? previousText,
        string currentText,
        float appliedThreshold,
        TimeSpan processingTime = default)
    {
        return new TextChangeWithGateResult
        {
            ChangePercentage = 1.0f,
            ShouldTranslate = true,
            Decision = GateDecision.TypewriterMaxDelayExceeded,
            AppliedThreshold = appliedThreshold,
            PreviousText = previousText,
            CurrentText = currentText,
            EditDistance = currentText.Length - (previousText?.Length ?? 0),
            ProcessingTime = processingTime
        };
    }

    /// <summary>
    /// [Issue #465] 静的UI要素（翻訳スキップ）の結果を作成
    /// </summary>
    public static TextChangeWithGateResult CreateStaticUiElement(
        string text,
        float appliedThreshold,
        TimeSpan processingTime = default)
    {
        return new TextChangeWithGateResult
        {
            ChangePercentage = 0.0f,
            ShouldTranslate = false,
            Decision = GateDecision.StaticUiElement,
            AppliedThreshold = appliedThreshold,
            PreviousText = text,
            CurrentText = text,
            EditDistance = 0,
            ProcessingTime = processingTime
        };
    }

    /// <summary>
    /// 大幅な長さ変化（翻訳実行）の結果を作成
    /// </summary>
    public static TextChangeWithGateResult CreateSignificantLengthChange(
        string? previousText,
        string currentText,
        float changePercentage,
        float appliedThreshold,
        TimeSpan processingTime = default)
    {
        return new TextChangeWithGateResult
        {
            ChangePercentage = changePercentage,
            ShouldTranslate = true,
            Decision = GateDecision.SignificantLengthChange,
            AppliedThreshold = appliedThreshold,
            PreviousText = previousText,
            CurrentText = currentText,
            EditDistance = Math.Abs((previousText?.Length ?? 0) - currentText.Length),
            ProcessingTime = processingTime
        };
    }
}
