using System;
using System.Runtime.Serialization;

namespace Baketa.Core.Translation.Exceptions;

/// <summary>
/// API応答フォーマットに関する翻訳例外
/// </summary>
[Serializable]
public class TranslationFormatException : TranslationException
{
    /// <summary>
    /// デフォルトコンストラクタ。CA1032警告対策用。
    /// </summary>
    public TranslationFormatException()
        : base(Models.TranslationErrorType.InvalidResponse, "API応答フォーマットに問題があります") { }
    
    /// <summary>
    /// 新しいフォーマット翻訳例外をメッセージ付きで初期化します
    /// </summary>
    public TranslationFormatException(string message)
        : base(Models.TranslationErrorType.InvalidResponse, message) { }
    
    /// <summary>
    /// 新しいフォーマット翻訳例外をメッセージと内部例外付きで初期化します
    /// </summary>
    public TranslationFormatException(string message, Exception innerException)
        : base(Models.TranslationErrorType.InvalidResponse, message, innerException) { }
        
    /// <summary>
    /// シリアライズ用コンストラクタ。CA1032警告対策用。
    /// </summary>
    [Obsolete("This API supports obsolete formatter-based serialization.", DiagnosticId = "SYSLIB0051")]
    protected TranslationFormatException(SerializationInfo info, StreamingContext context)
        : base(info, context) { }
}

/// <summary>
/// 翻訳サービスの可用性に関する例外
/// </summary>
[Serializable]
public class TranslationServiceException : TranslationException
{
    /// <summary>
    /// デフォルトコンストラクタ。CA1032警告対策用。
    /// </summary>
    public TranslationServiceException()
        : base(Models.TranslationErrorType.ServiceUnavailable, "翻訳サービスが利用できません") { }
    
    /// <summary>
    /// 新しいサービス翻訳例外をメッセージ付きで初期化します
    /// </summary>
    public TranslationServiceException(string message)
        : base(Models.TranslationErrorType.ServiceUnavailable, message) { }
    
    /// <summary>
    /// 新しいサービス翻訳例外をメッセージと内部例外付きで初期化します
    /// </summary>
    public TranslationServiceException(string message, Exception innerException)
        : base(Models.TranslationErrorType.ServiceUnavailable, message, innerException) { }
        
    /// <summary>
    /// シリアライズ用コンストラクタ。CA1032警告対策用。
    /// </summary>
    [Obsolete("This API supports obsolete formatter-based serialization.", DiagnosticId = "SYSLIB0051")]
    protected TranslationServiceException(SerializationInfo info, StreamingContext context)
        : base(info, context) { }
}

/// <summary>
/// 翻訳リクエスト自体の問題に関する例外
/// </summary>
[Serializable]
public class TranslationRequestException : TranslationException
{
    /// <summary>
    /// デフォルトコンストラクタ。CA1032警告対策用。
    /// </summary>
    public TranslationRequestException()
        : base(Models.TranslationErrorType.InvalidRequest, "翻訳リクエストが無効です") { }
    
    /// <summary>
    /// 新しいリクエスト翻訳例外をメッセージ付きで初期化します
    /// </summary>
    public TranslationRequestException(string message)
        : base(Models.TranslationErrorType.InvalidRequest, message) { }
    
    /// <summary>
    /// 新しいリクエスト翻訳例外をメッセージと内部例外付きで初期化します
    /// </summary>
    public TranslationRequestException(string message, Exception innerException)
        : base(Models.TranslationErrorType.InvalidRequest, message, innerException) { }
        
    /// <summary>
    /// シリアライズ用コンストラクタ。CA1032警告対策用。
    /// </summary>
    [Obsolete("This API supports obsolete formatter-based serialization.", DiagnosticId = "SYSLIB0051")]
    protected TranslationRequestException(SerializationInfo info, StreamingContext context)
        : base(info, context) { }
}

/// <summary>
/// 翻訳認証に関する例外
/// </summary>
[Serializable]
public class TranslationAuthException : TranslationException
{
    /// <summary>
    /// デフォルトコンストラクタ。CA1032警告対策用。
    /// </summary>
    public TranslationAuthException()
        : base(Models.TranslationErrorType.AuthError, "認証に失敗しました") { }
    
    /// <summary>
    /// 新しい認証翻訳例外をメッセージ付きで初期化します
    /// </summary>
    public TranslationAuthException(string message)
        : base(Models.TranslationErrorType.AuthError, message) { }
    
    /// <summary>
    /// 新しい認証翻訳例外をメッセージと内部例外付きで初期化します
    /// </summary>
    public TranslationAuthException(string message, Exception innerException)
        : base(Models.TranslationErrorType.AuthError, message, innerException) { }
        
    /// <summary>
    /// シリアライズ用コンストラクタ。CA1032警告対策用。
    /// </summary>
    [Obsolete("This API supports obsolete formatter-based serialization.", DiagnosticId = "SYSLIB0051")]
    protected TranslationAuthException(SerializationInfo info, StreamingContext context)
        : base(info, context) { }
}

/// <summary>
/// 翻訳モデルに関する例外
/// </summary>
[Serializable]
public class TranslationModelException : TranslationException
{
    /// <summary>
    /// デフォルトコンストラクタ。CA1032警告対策用。
    /// </summary>
    public TranslationModelException()
        : base(Models.TranslationErrorType.ModelError, "モデルエラーが発生しました") { }
    
    /// <summary>
    /// 新しいモデル翻訳例外をメッセージ付きで初期化します
    /// </summary>
    public TranslationModelException(string message)
        : base(Models.TranslationErrorType.ModelError, message) { }
    
    /// <summary>
    /// 新しいモデル翻訳例外をメッセージと内部例外付きで初期化します
    /// </summary>
    public TranslationModelException(string message, Exception innerException)
        : base(Models.TranslationErrorType.ModelError, message, innerException) { }
        
    /// <summary>
    /// シリアライズ用コンストラクタ。CA1032警告対策用。
    /// </summary>
    [Obsolete("This API supports obsolete formatter-based serialization.", DiagnosticId = "SYSLIB0051")]
    protected TranslationModelException(SerializationInfo info, StreamingContext context)
        : base(info, context) { }
}

/// <summary>
/// 操作タイムアウトに関する例外
/// </summary>
[Serializable]
public class TranslationTimeoutException : TranslationException
{
    /// <summary>
    /// デフォルトコンストラクタ。CA1032警告対策用。
    /// </summary>
    public TranslationTimeoutException()
        : base(Models.TranslationErrorType.Timeout, "操作がタイムアウトしました") { }
    
    /// <summary>
    /// 新しいタイムアウト翻訳例外をメッセージ付きで初期化します
    /// </summary>
    public TranslationTimeoutException(string message)
        : base(Models.TranslationErrorType.Timeout, message) { }
    
    /// <summary>
    /// 新しいタイムアウト翻訳例外をメッセージと内部例外付きで初期化します
    /// </summary>
    public TranslationTimeoutException(string message, Exception innerException)
        : base(Models.TranslationErrorType.Timeout, message, innerException) { }
        
    /// <summary>
    /// シリアライズ用コンストラクタ。CA1032警告対策用。
    /// </summary>
    [Obsolete("This API supports obsolete formatter-based serialization.", DiagnosticId = "SYSLIB0051")]
    protected TranslationTimeoutException(SerializationInfo info, StreamingContext context)
        : base(info, context) { }
}