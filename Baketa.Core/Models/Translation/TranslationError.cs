using System;

namespace Baketa.Core.Models.Translation
{
    /// <summary>
    /// 翻訳エラーを表すクラス
    /// </summary>
    public class TranslationError
    {
        /// <summary>
        /// エラーコード
        /// 
        /// 標準エラーコード:
        /// - NetworkError: ネットワーク接続の問題
        /// - AuthenticationError: 認証失敗（APIキー無効など）
        /// - QuotaExceeded: 利用制限超過
        /// - UnsupportedLanguagePair: サポートされていない言語ペア
        /// - ServiceUnavailable: サービス停止中
        /// - InvalidRequest: 不正なリクエスト形式
        /// - InternalError: 内部エラー
        /// - TimeoutError: タイムアウト
        /// </summary>
        public required string ErrorCode { get; set; }

        /// <summary>
        /// エラーメッセージ
        /// </summary>
        public required string Message { get; set; }

        /// <summary>
        /// 詳細なエラー情報
        /// </summary>
        public string? Details { get; set; }

        /// <summary>
        /// エラーの原因となった例外
        /// </summary>
        public Exception? Exception { get; set; }

        /// <summary>
        /// 標準エラーコード: ネットワークエラー
        /// </summary>
        public const string NetworkError = "NetworkError";

        /// <summary>
        /// 標準エラーコード: 認証エラー
        /// </summary>
        public const string AuthenticationError = "AuthenticationError";

        /// <summary>
        /// 標準エラーコード: クォータ超過
        /// </summary>
        public const string QuotaExceeded = "QuotaExceeded";

        /// <summary>
        /// 標準エラーコード: サポートされていない言語ペア
        /// </summary>
        public const string UnsupportedLanguagePair = "UnsupportedLanguagePair";

        /// <summary>
        /// 標準エラーコード: サービス利用不可
        /// </summary>
        public const string ServiceUnavailable = "ServiceUnavailable";

        /// <summary>
        /// 標準エラーコード: 無効なリクエスト
        /// </summary>
        public const string InvalidRequest = "InvalidRequest";

        /// <summary>
        /// 標準エラーコード: 内部エラー
        /// </summary>
        public const string InternalError = "InternalError";

        /// <summary>
        /// 標準エラーコード: タイムアウト
        /// </summary>
        public const string TimeoutError = "TimeoutError";

        /// <summary>
        /// エラーを作成します
        /// </summary>
        /// <param name="errorCode">エラーコード</param>
        /// <param name="message">エラーメッセージ</param>
        /// <returns>エラーインスタンス</returns>
        public static TranslationError Create(string errorCode, string message)
        {
            ArgumentNullException.ThrowIfNull(errorCode);
            ArgumentNullException.ThrowIfNull(message);
            
            return new TranslationError
            {
                ErrorCode = errorCode,
                Message = message
            };
        }

        /// <summary>
        /// 例外からエラーを作成します
        /// </summary>
        /// <param name="errorCode">エラーコード</param>
        /// <param name="message">エラーメッセージ</param>
        /// <param name="exception">例外</param>
        /// <returns>エラーインスタンス</returns>
        public static TranslationError FromException(string errorCode, string message, Exception exception)
        {
            ArgumentNullException.ThrowIfNull(errorCode);
            ArgumentNullException.ThrowIfNull(message);
            ArgumentNullException.ThrowIfNull(exception);
            
            return new TranslationError
            {
                ErrorCode = errorCode,
                Message = message,
                Details = exception.ToString(),
                Exception = exception
            };
        }
    }
}
