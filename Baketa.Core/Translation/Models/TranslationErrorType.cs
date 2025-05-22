namespace Baketa.Core.Translation.Models;

/// <summary>
/// 翻訳エラーの種類
/// </summary>
public enum TranslationErrorType
{
    /// <summary>
    /// 不明なエラー
    /// </summary>
    Unknown = 0,
    
    /// <summary>
    /// ネットワークエラー
    /// </summary>
    Network = 1,
    
    /// <summary>
    /// API認証エラー
    /// </summary>
    Authentication = 2,
    
    /// <summary>
    /// 上限超過エラー
    /// </summary>
    QuotaExceeded = 3,
    
    /// <summary>
    /// 翻訳エンジンエラー
    /// </summary>
    Engine = 4,
    
    /// <summary>
    /// サポートされていない言語エラー
    /// </summary>
    UnsupportedLanguage = 5,
    
    /// <summary>
    /// 入力テキスト無効エラー
    /// </summary>
    InvalidInput = 6,
    
    /// <summary>
    /// タイムアウトエラー
    /// </summary>
    Timeout = 7,
    
    /// <summary>
    /// 例外エラー
    /// </summary>
    Exception = 8,
    
    /// <summary>
    /// ネットワークエラー（GeminiEngine用）
    /// </summary>
    NetworkError = 9,
    
    /// <summary>
    /// 処理エラー（エンジン内部処理エラー）
    /// </summary>
    ProcessingError = 10,
    
    /// <summary>
    /// サービス利用不可エラー
    /// </summary>
    ServiceUnavailable = 11,
    
    /// <summary>
    /// モデルロードエラー
    /// </summary>
    ModelLoadError = 12,
    
    /// <summary>
    /// 無効なリクエストエラー
    /// </summary>
    InvalidRequest = 13,
    
    /// <summary>
    /// 無効なレスポンスエラー
    /// </summary>
    InvalidResponse = 14,
    
    /// <summary>
    /// 操作がキャンセルされたエラー
    /// </summary>
    OperationCanceled = 15,
    
    /// <summary>
    /// 予期しないエラー
    /// </summary>
    UnexpectedError = 16,
    
    /// <summary>
    /// 認証エラー
    /// </summary>
    AuthError = 17,
    
    /// <summary>
    /// レート制限超過エラー
    /// </summary>
    RateLimitExceeded = 18,
    
    /// <summary>
    /// モデルエラー
    /// </summary>
    ModelError = 19
}