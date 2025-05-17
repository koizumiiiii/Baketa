using System;

namespace Baketa.Core.Models.Translation
{
    /// <summary>
    /// 翻訳エラー情報を表すクラス
    /// </summary>
    [Obsolete("代わりに Baketa.Core.Translation.Models.TranslationError を使用してください。", false)]
    public class TranslationError
    {
        /// <summary>
        /// エラーコード
        /// </summary>
        public string ErrorCode { get; set; } = string.Empty;
        
        /// <summary>
        /// エラーメッセージ
        /// </summary>
        public string Message { get; set; } = string.Empty;
        
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
        /// エラーの種類
        /// </summary>
        public TranslationErrorType ErrorType { get; set; } = TranslationErrorType.Unknown;
        
        /// <summary>
        /// デフォルトコンストラクタ
        /// </summary>
        public TranslationError()
        {
        }
        
        /// <summary>
        /// 基本情報を指定して初期化
        /// </summary>
        /// <param name="errorCode">エラーコード</param>
        /// <param name="message">メッセージ</param>
        /// <param name="isRetryable">再試行可能かどうか</param>
        /// <param name="type">エラーの種類</param>
        public TranslationError(string errorCode, string message, bool isRetryable = false, TranslationErrorType errorType = TranslationErrorType.Unknown)
        {
            ErrorCode = errorCode ?? throw new ArgumentNullException(nameof(errorCode));
            Message = message ?? throw new ArgumentNullException(nameof(message));
            IsRetryable = isRetryable;
            ErrorType = errorType;
        }
        
        /// <summary>
        /// 例外からエラーを初期化
        /// </summary>
        /// <param name="errorCode">エラーコード</param>
        /// <param name="exception">例外</param>
        /// <param name="isRetryable">再試行可能かどうか</param>
        /// <param name="type">エラーの種類</param>
        public TranslationError(string errorCode, Exception exception, bool isRetryable = false, TranslationErrorType errorType = TranslationErrorType.Exception)
        {
            ErrorCode = errorCode ?? throw new ArgumentNullException(nameof(errorCode));
            Exception = exception ?? throw new ArgumentNullException(nameof(exception));
            Message = exception.Message;
            Details = exception.StackTrace;
            IsRetryable = isRetryable;
            ErrorType = errorType;
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
        
        /// <summary>
        /// 文字列表現を返す
        /// </summary>
        public override string ToString()
        {
            return $"[{ErrorCode}] {Message}" + (Details != null ? $" - {Details}" : "");
        }
        
        /// <summary>
        /// エラーオブジェクトを作成します
        /// </summary>
        /// <param name="errorCode">エラーコード</param>
        /// <param name="message">エラーメッセージ</param>
        /// <param name="isRetryable">再試行可能かどうか</param>
        /// <param name="errorType">エラー種別</param>
        /// <returns>新しいエラーオブジェクト</returns>
        public static TranslationError Create(
            string errorCode,
            string message,
            bool isRetryable = false,
            TranslationErrorType errorType = TranslationErrorType.Unknown)
        {
            return new TranslationError(errorCode, message, isRetryable, errorType);
        }
        
        /// <summary>
        /// 例外からエラーオブジェクトを作成します
        /// </summary>
        /// <param name="errorCode">エラーコード</param>
        /// <param name="message">エラーメッセージ</param>
        /// <param name="exception">例外</param>
        /// <param name="isRetryable">再試行可能かどうか</param>
        /// <param name="errorType">エラー種別</param>
        /// <returns>新しいエラーオブジェクト</returns>
        public static TranslationError FromException(
            string errorCode,
            string message,
            Exception exception,
            bool isRetryable = false,
            TranslationErrorType errorType = TranslationErrorType.Exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
            var error = new TranslationError(errorCode, message, isRetryable, errorType)
            {
                Exception = exception,
                Details = exception.StackTrace
            };
            return error;
        }
        
        /// <summary>
        /// 暗黙的変換演算子 - 元のエラーオブジェクトを新しい名前空間のオブジェクトに変換
        /// </summary>
        /// <param name="error">変換するエラーオブジェクト</param>
        /// <returns>変換後のエラーオブジェクト、またはnull</returns>
        public static implicit operator Baketa.Core.Translation.Models.TranslationError?(TranslationError? error)
        {
            return error == null ? null : new Baketa.Core.Translation.Models.TranslationError
            {
                ErrorCode = error.ErrorCode,
                Message = error.Message,
                Details = error.Details,
                Exception = error.Exception,
                IsRetryable = error.IsRetryable,
                ErrorType = (Baketa.Core.Translation.Models.TranslationErrorType)(int)error.ErrorType
            };
        }
        
        /// <summary>
        /// 新しい名前空間のエラーオブジェクトに変換するメソッド
        /// （暗黙的変換演算子と同等の機能）
        /// </summary>
        /// <returns>新しい名前空間のエラーオブジェクト</returns>
        public Baketa.Core.Translation.Models.TranslationError ToTranslationError()
        {
            // nullチェックは暗黙的変換演算子内で行われるため、このメソッド内では不要
            // この型のインスタンスメソッドなので、thisがnullになることはない
            return (Baketa.Core.Translation.Models.TranslationError)this;
        }
        
        /// <summary>
        /// 明示的変換演算子 - 新しい名前空間のエラーオブジェクトを元のオブジェクトに変換
        /// </summary>
        /// <param name="error">変換するエラーオブジェクト</param>
        /// <returns>変換後のエラーオブジェクト、またはnull</returns>
        public static explicit operator TranslationError?(Baketa.Core.Translation.Models.TranslationError? error)
        {
            return error == null ? null : new TranslationError
            {
                ErrorCode = error.ErrorCode,
                Message = error.Message,
                Details = error.Details,
                Exception = error.Exception,
                IsRetryable = error.IsRetryable,
                ErrorType = (TranslationErrorType)(int)error.ErrorType
            };
        }
        
        /// <summary>
        /// 新しい名前空間のエラーオブジェクトから変換するメソッド
        /// （明示的変換演算子と同等の機能）
        /// </summary>
        /// <param name="error">新しい名前空間のエラーオブジェクト</param>
        /// <returns>元のエラーオブジェクト</returns>
        public static TranslationError? FromTranslationError(Baketa.Core.Translation.Models.TranslationError? error)
        {
            return (TranslationError?)error;
        }
    }
}