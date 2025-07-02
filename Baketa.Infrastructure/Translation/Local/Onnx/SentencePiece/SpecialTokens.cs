namespace Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece;

/// <summary>
/// 特殊トークンの情報
/// </summary>
public class SpecialTokens
{
    /// <summary>
    /// 未知トークンの ID
    /// </summary>
    public int UnknownId { get; set; } = -1;

    /// <summary>
    /// 文開始トークンの ID
    /// </summary>
    public int BeginOfSentenceId { get; set; } = -1;

    /// <summary>
    /// 文終了トークンの ID
    /// </summary>
    public int EndOfSentenceId { get; set; } = -1;

    /// <summary>
    /// パディングトークンの ID
    /// </summary>
    public int PaddingId { get; set; } = -1;
}
