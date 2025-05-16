namespace Baketa.Core.Models.Translation
{
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
        Exception = 8
    }
}