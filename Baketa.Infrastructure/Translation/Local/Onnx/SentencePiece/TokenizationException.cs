using System;

namespace Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece;

/// <summary>
/// トークン化処理で発生する例外
/// </summary>
public class TokenizationException : Exception
{
    /// <summary>
    /// エラーが発生した入力テキスト
    /// </summary>
    public string InputText { get; init; } = string.Empty;

    /// <summary>
    /// エラーが発生した文字位置（該当する場合）
    /// </summary>
    public int? CharacterPosition { get; init; }

    /// <summary>
    /// 使用していたモデル名
    /// </summary>
    public string ModelName { get; init; } = string.Empty;

    /// <summary>
    /// デフォルトコンストラクタ
    /// </summary>
    public TokenizationException()
    {
    }

    /// <summary>
    /// メッセージ付きコンストラクタ
    /// </summary>
    /// <param name="message">エラーメッセージ</param>
    public TokenizationException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// メッセージと内部例外付きコンストラクタ
    /// </summary>
    /// <param name="message">エラーメッセージ</param>
    /// <param name="innerException">内部例外</param>
    public TokenizationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// 完全な情報付きコンストラクタ
    /// </summary>
    /// <param name="message">エラーメッセージ</param>
    /// <param name="inputText">入力テキスト</param>
    /// <param name="modelName">モデル名</param>
    /// <param name="innerException">内部例外</param>
    public TokenizationException(
        string message,
        string inputText,
        string modelName,
        Exception? innerException = null)
        : base(message, innerException)
    {
        InputText = inputText;
        ModelName = modelName;
    }

    /// <summary>
    /// 文字位置付きコンストラクタ
    /// </summary>
    /// <param name="message">エラーメッセージ</param>
    /// <param name="inputText">入力テキスト</param>
    /// <param name="modelName">モデル名</param>
    /// <param name="characterPosition">文字位置</param>
    /// <param name="innerException">内部例外</param>
    public TokenizationException(
        string message,
        string inputText,
        string modelName,
        int characterPosition,
        Exception? innerException = null)
        : base(message, innerException)
    {
        InputText = inputText;
        ModelName = modelName;
        CharacterPosition = characterPosition;
    }
}
