using Baketa.Core.Models.Processing;

namespace Baketa.Core.Abstractions.Processing;

/// <summary>
/// テキスト変化検知サービス
/// OCR結果テキストの効率的な変化検知を提供
/// Geminiフィードバック反映: スレッドセーフ実装とコンテキスト別管理
/// </summary>
public interface ITextChangeDetectionService
{
    /// <summary>
    /// テキスト変化を検知
    /// </summary>
    /// <param name="previousText">前回のテキスト</param>
    /// <param name="currentText">現在のテキスト</param>
    /// <param name="contextId">処理コンテキストID（ウィンドウハンドル等）</param>
    /// <returns>テキスト変化結果</returns>
    Task<TextChangeResult> DetectTextChangeAsync(string previousText, string currentText, string contextId);

    /// <summary>
    /// 編集距離を計算
    /// </summary>
    /// <param name="text1">比較対象テキスト1</param>
    /// <param name="text2">比較対象テキスト2</param>
    /// <returns>編集距離</returns>
    float CalculateEditDistance(string text1, string text2);

    /// <summary>
    /// 有意なテキスト変化かどうかを判定
    /// </summary>
    /// <param name="changePercentage">変化率</param>
    /// <param name="threshold">閾値</param>
    /// <returns>有意な変化かどうか</returns>
    bool IsSignificantTextChange(float changePercentage, float threshold = 0.1f);

    /// <summary>
    /// 特定コンテキストの前回テキストをクリア
    /// </summary>
    /// <param name="contextId">処理コンテキストID</param>
    void ClearPreviousText(string contextId);

    /// <summary>
    /// 全コンテキストの前回テキストをクリア
    /// </summary>
    void ClearAllPreviousTexts();

    /// <summary>
    /// キャッシュから前回テキストを取得
    /// [Issue #230] OCR完了後のテキスト変化検知に使用
    /// </summary>
    /// <param name="contextId">処理コンテキストID（ウィンドウハンドル等）</param>
    /// <returns>前回のテキスト（初回の場合はnull）</returns>
    string? GetPreviousText(string contextId);

    /// <summary>
    /// 前回テキストをキャッシュに保存
    /// [Issue #230] OCR完了後のテキスト変化検知に使用
    /// </summary>
    /// <param name="contextId">処理コンテキストID（ウィンドウハンドル等）</param>
    /// <param name="text">保存するテキスト</param>
    void SetPreviousText(string contextId, string text);
}

/// <summary>
/// テキスト変化検知結果
/// </summary>
public sealed record TextChangeResult
{
    /// <summary>
    /// テキストが変化したかどうか
    /// </summary>
    public required bool HasChanged { get; init; }

    /// <summary>
    /// 変化率 (0.0-1.0)
    /// </summary>
    public required float ChangePercentage { get; init; }

    /// <summary>
    /// 編集距離
    /// </summary>
    public float EditDistance { get; init; }

    /// <summary>
    /// 前回テキストの長さ
    /// </summary>
    public int PreviousLength { get; init; }

    /// <summary>
    /// 現在テキストの長さ
    /// </summary>
    public int CurrentLength { get; init; }

    /// <summary>
    /// 処理時間
    /// </summary>
    public TimeSpan ProcessingTime { get; init; }

    /// <summary>
    /// 使用されたアルゴリズム
    /// </summary>
    public TextChangeAlgorithmType AlgorithmUsed { get; init; }

    /// <summary>
    /// 前回テキスト（デバッグ用）
    /// </summary>
    public string? PreviousText { get; init; }

    /// <summary>
    /// 現在テキスト（デバッグ用）
    /// </summary>
    public string? CurrentText { get; init; }

    /// <summary>
    /// 変化なしの結果を作成
    /// </summary>
    public static TextChangeResult CreateNoChange(string previousText, TimeSpan processingTime = default)
    {
        return new TextChangeResult
        {
            HasChanged = false,
            ChangePercentage = 0f,
            EditDistance = 0f,
            PreviousLength = previousText?.Length ?? 0,
            CurrentLength = previousText?.Length ?? 0,
            ProcessingTime = processingTime,
            AlgorithmUsed = TextChangeAlgorithmType.EditDistance,
            PreviousText = previousText,
            CurrentText = previousText
        };
    }

    /// <summary>
    /// 有意な変化の結果を作成
    /// </summary>
    public static TextChangeResult CreateSignificantChange(string? previousText, string currentText, float changePercentage = 1.0f, TimeSpan processingTime = default)
    {
        return new TextChangeResult
        {
            HasChanged = true,
            ChangePercentage = changePercentage,
            EditDistance = previousText?.Length ?? currentText.Length,
            PreviousLength = previousText?.Length ?? 0,
            CurrentLength = currentText.Length,
            ProcessingTime = processingTime,
            AlgorithmUsed = TextChangeAlgorithmType.EditDistance,
            PreviousText = previousText,
            CurrentText = currentText
        };
    }

    /// <summary>
    /// 初回実行時の結果を作成
    /// </summary>
    public static TextChangeResult CreateFirstTime(string currentText, TimeSpan processingTime = default)
    {
        return new TextChangeResult
        {
            HasChanged = true, // 初回は変化ありとして処理継続
            ChangePercentage = 1.0f,
            EditDistance = 0f,
            PreviousLength = 0,
            CurrentLength = currentText.Length,
            ProcessingTime = processingTime,
            AlgorithmUsed = TextChangeAlgorithmType.EditDistance,
            PreviousText = null,
            CurrentText = currentText
        };
    }
}

/// <summary>
/// テキスト変化検知アルゴリズムタイプ
/// </summary>
public enum TextChangeAlgorithmType
{
    /// <summary>
    /// 編集距離 (Levenshtein Distance)
    /// </summary>
    EditDistance,

    /// <summary>
    /// ハッシュ比較
    /// </summary>
    HashComparison,

    /// <summary>
    /// 文字列完全一致
    /// </summary>
    ExactMatch
}
