using System;
using System.Runtime.Serialization;
using Baketa.Core.Translation.Models;

namespace Baketa.Core.Translation.Exceptions;

/// <summary>
/// 翻訳処理に関する一般例外
/// </summary>
[Serializable]
public class TranslationException : TranslationBaseException
{
    /// <summary>
    /// エラータイプ
    /// </summary>
    public TranslationErrorType ErrorType { get; }

    /// <summary>
    /// 新しい翻訳例外を初期化します
    /// </summary>
    public TranslationException()
        : base()
    {
        ErrorType = TranslationErrorType.Unknown;
    }

    /// <summary>
    /// 新しい翻訳例外をメッセージ付きで初期化します
    /// </summary>
    public TranslationException(string message)
        : base(message)
    {
        ErrorType = TranslationErrorType.Unknown;
    }

    /// <summary>
    /// 新しい翻訳例外をエラータイプとメッセージ付きで初期化します
    /// </summary>
    public TranslationException(TranslationErrorType errorType, string message)
        : base(message)
    {
        ErrorType = errorType;
    }

    /// <summary>
    /// 新しい翻訳例外をメッセージと内部例外付きで初期化します
    /// </summary>
    public TranslationException(string message, Exception innerException)
        : base(message, innerException)
    {
        ErrorType = TranslationErrorType.Unknown;
    }

    /// <summary>
    /// 新しい翻訳例外をエラータイプ、メッセージと内部例外付きで初期化します
    /// </summary>
    public TranslationException(TranslationErrorType errorType, string message, Exception innerException)
        : base(message, innerException)
    {
        ErrorType = errorType;
    }

    /// <summary>
    /// 新しい翻訳例外を詳細情報付きで初期化します
    /// </summary>
    public TranslationException(
        TranslationErrorType errorType,
        string message,
        string errorCode,
        string locationInfo = "",
        bool isRetryable = false,
        Exception? innerException = null)
        : base(message, errorCode, locationInfo, isRetryable, innerException)
    {
        ErrorType = errorType;
    }
    
    /// <summary>
    /// シリアライズ用コンストラクタ
    /// </summary>
    [Obsolete("This API supports obsolete formatter-based serialization.", DiagnosticId = "SYSLIB0051")]
    protected TranslationException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
        // シリアライズされたプロパティを取得
        ErrorType = (TranslationErrorType)info.GetInt32(nameof(ErrorType));
    }
    
    /// <summary>
    /// シリアライズ時にオブジェクトデータを保存
    /// </summary>
    [System.Security.SecurityCritical]
    [Obsolete("This API supports obsolete formatter-based serialization.", DiagnosticId = "SYSLIB0051")]
    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        ArgumentNullException.ThrowIfNull(info, nameof(info));
            
        info.AddValue(nameof(ErrorType), (int)ErrorType);
        
        base.GetObjectData(info, context);
    }
}
