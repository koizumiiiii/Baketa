using System;
using System.Threading.Tasks;

namespace Baketa.Core.Services;

    /// <summary>
    /// 設定管理サービスインターフェース
    /// </summary>
    public interface ISettingsService
    {
        /// <summary>
        /// 設定値を取得します
        /// </summary>
        /// <typeparam name="T">値の型</typeparam>
        /// <param name="key">設定キー</param>
        /// <param name="defaultValue">デフォルト値</param>
        /// <returns>設定値、存在しない場合はデフォルト値</returns>
        T GetValue<T>(string key, T defaultValue);
        
        /// <summary>
        /// 設定値を設定します
        /// </summary>
        /// <typeparam name="T">値の型</typeparam>
        /// <param name="key">設定キー</param>
        /// <param name="value">設定値</param>
        void SetValue<T>(string key, T value);
        
        /// <summary>
        /// 設定が存在するか確認します
        /// </summary>
        /// <param name="key">設定キー</param>
        /// <returns>存在する場合はtrue</returns>
        bool HasValue(string key);
        
        /// <summary>
        /// 設定を削除します
        /// </summary>
        /// <param name="key">設定キー</param>
        void RemoveValue(string key);
        
        /// <summary>
        /// 変更をファイルに保存します
        /// </summary>
        /// <returns>保存タスク</returns>
        Task SaveAsync();
        
        /// <summary>
        /// 設定を再読み込みします
        /// </summary>
        /// <returns>読み込みタスク</returns>
        Task ReloadAsync();
    }
