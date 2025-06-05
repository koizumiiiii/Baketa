namespace Baketa.Core.Abstractions.Common;

    /// <summary>
    /// 設定可能なオブジェクトを表すインターフェース
    /// </summary>
    /// <typeparam name="TConfig">設定の型</typeparam>
    public interface IConfigurable<TConfig> where TConfig : class
    {
        /// <summary>
        /// 設定を適用します
        /// </summary>
        /// <param name="config">適用する設定</param>
        void Configure(TConfig config);
        
        /// <summary>
        /// 現在の設定を取得します
        /// </summary>
        /// <returns>現在の設定</returns>
        TConfig GetConfiguration();
        
        /// <summary>
        /// デフォルト設定を取得します
        /// </summary>
        /// <returns>デフォルト設定</returns>
        TConfig GetDefaultConfiguration();
        
        /// <summary>
        /// 設定をリセットします
        /// </summary>
        void ResetConfiguration();
    }
