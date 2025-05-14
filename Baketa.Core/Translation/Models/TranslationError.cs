using System;

namespace Baketa.Core.Translation.Models
{
    /// <summary>
    /// 翻訳エラー情報を表すクラス
    /// </summary>
    public class TranslationError
    {
        /// <summary>
        /// エラーコード
        /// </summary>
        public required string ErrorCode { get; set; }
        
        /// <summary>
        /// エラーメッセージ
        /// </summary>
        public required string Message { get; set; }
        
        /// <summary>
        /// 詳細
        /// </summary>
        public string? Details { get; set; }
        
        /// <summary>
        /// 例外情報
        /// </summary>
        public Exception? Exception { get; set; }

        /// <summary>
        /// 再試行可能かどうか
        /// </summary>
        public bool IsRetryable { get; set; }
        
        /// <summary>
        /// デフォルトコンストラクタ
        /// </summary>
        public TranslationError()
        {
        }
        
        /// <summary>
        /// 基本情報を指定してエラーを初期化
        /// </summary>
        /// <param name="errorCode">エラーコード</param>
        /// <param name="message">メッセージ</param>
        /// <param name="isRetryable">再試行可能かどうか</param>
        public TranslationError(string errorCode, string message, bool isRetryable = false)
        {
            ArgumentNullException.ThrowIfNull(errorCode);
            ArgumentNullException.ThrowIfNull(message);
            ErrorCode = errorCode;
            Message = message;
            IsRetryable = isRetryable;
        }
        
        /// <summary>
        /// 例外からエラーを初期化
        /// </summary>
        /// <param name="errorCode">エラーコード</param>
        /// <param name="exception">例外</param>
        /// <param name="isRetryable">再試行可能かどうか</param>
        public TranslationError(string errorCode, Exception exception, bool isRetryable = false)
        {
            ArgumentNullException.ThrowIfNull(errorCode);
            ArgumentNullException.ThrowIfNull(exception);
            ErrorCode = errorCode;
            Message = exception.Message;
            Exception = exception;
            Details = exception.StackTrace;
            IsRetryable = isRetryable;
        }

        /// <summary>
        /// クローンを作成
        /// </summary>
        /// <returns>このエラーのクローン</returns>
        public TranslationError Clone()
        {
            return new TranslationError
            {
                ErrorCode = ErrorCode,
                Message = Message,
                Details = Details,
                Exception = Exception,
                IsRetryable = IsRetryable
            };
        }

        /// <summary>
        /// 文字列表現を返す
        /// </summary>
        public override string ToString()
        {
            return $"[{ErrorCode}] {Message}" + (Details != null ? $" - {Details}" : "");
        }
        
        #region 標準エラーコード定数
        
        /// <summary>
        /// ネットワークエラー
        /// </summary>
        public static readonly string NetworkError = "NetworkError";
        
        /// <summary>
        /// 認証エラー
        /// </summary>
        public static readonly string AuthenticationError = "AuthenticationError";
        
        /// <summary>
        /// クォータ超過
        /// </summary>
        public static readonly string QuotaExceeded = "QuotaExceeded";
        
        /// <summary>
        /// サービス利用不可
        /// </summary>
        public static readonly string ServiceUnavailable = "ServiceUnavailable";
        
        /// <summary>
        /// サポートされていない言語ペア
        /// </summary>
        public static readonly string UnsupportedLanguagePair = "UnsupportedLanguagePair";
        
        /// <summary>
        /// 無効なリクエスト
        /// </summary>
        public static readonly string InvalidRequest = "InvalidRequest";
        
        /// <summary>
        /// 内部エラー
        /// </summary>
        public static readonly string InternalError = "InternalError";
        
        /// <summary>
        /// タイムアウトエラー
        /// </summary>
        public static readonly string TimeoutError = "TimeoutError";
        
        #endregion
    }
}
