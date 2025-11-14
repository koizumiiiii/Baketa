using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Baketa.Core.Events;
using Baketa.Core.Settings;

namespace Baketa.Core.Services;

/// <summary>
/// 設定管理サービスインターフェース（強化版）
/// 型安全な設定管理、変更通知、プロファイル管理をサポート
/// </summary>
public interface ISettingsService
{
    #region 基本設定操作

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

    #endregion

    #region 型安全な設定操作

    /// <summary>
    /// アプリケーション設定全体を取得します
    /// </summary>
    /// <returns>アプリケーション設定</returns>
    AppSettings GetSettings();

    /// <summary>
    /// アプリケーション設定全体を設定します
    /// </summary>
    /// <param name="settings">アプリケーション設定</param>
    Task SetSettingsAsync(AppSettings settings);

    /// <summary>
    /// 特定カテゴリの設定を取得します
    /// </summary>
    /// <typeparam name="T">設定カテゴリの型</typeparam>
    /// <returns>設定カテゴリ</returns>
    T GetCategorySettings<T>() where T : class, new();

    /// <summary>
    /// 特定カテゴリの設定を更新します
    /// </summary>
    /// <typeparam name="T">設定カテゴリの型</typeparam>
    /// <param name="settings">設定カテゴリ</param>
    Task SetCategorySettingsAsync<T>(T settings) where T : class, new();

    /// <summary>
    /// 設定を非同期で取得します
    /// </summary>
    /// <typeparam name="T">設定の型</typeparam>
    /// <returns>設定オブジェクト</returns>
    Task<T?> GetAsync<T>() where T : class, new();

    /// <summary>
    /// 設定を非同期で保存します
    /// </summary>
    /// <typeparam name="T">設定の型</typeparam>
    /// <param name="settings">保存する設定</param>
    /// <returns>保存タスク</returns>
    Task SaveAsync<T>(T settings) where T : class, new();

    #endregion

    #region プロファイル管理

    /// <summary>
    /// ゲームプロファイルを取得します
    /// </summary>
    /// <param name="profileId">プロファイルID</param>
    /// <returns>ゲームプロファイル、存在しない場合はnull</returns>
    GameProfileSettings? GetGameProfile(string profileId);

    /// <summary>
    /// ゲームプロファイルを保存します
    /// </summary>
    /// <param name="profileId">プロファイルID</param>
    /// <param name="profile">ゲームプロファイル</param>
    Task SaveGameProfileAsync(string profileId, GameProfileSettings profile);

    /// <summary>
    /// ゲームプロファイルを削除します
    /// </summary>
    /// <param name="profileId">プロファイルID</param>
    Task DeleteGameProfileAsync(string profileId);

    /// <summary>
    /// 全ゲームプロファイルを取得します
    /// </summary>
    /// <returns>ゲームプロファイルの辞書</returns>
    IReadOnlyDictionary<string, GameProfileSettings> GetAllGameProfiles();

    /// <summary>
    /// アクティブなゲームプロファイルを設定します
    /// </summary>
    /// <param name="profileId">プロファイルID（nullで無効化）</param>
    Task SetActiveGameProfileAsync(string? profileId);

    /// <summary>
    /// アクティブなゲームプロファイルを取得します
    /// </summary>
    /// <returns>アクティブなプロファイル、存在しない場合はnull</returns>
    GameProfileSettings? GetActiveGameProfile();

    #endregion

    #region 永続化操作

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

    /// <summary>
    /// 設定をリセットします（デフォルト値に戻す）
    /// </summary>
    /// <returns>リセットタスク</returns>
    Task ResetToDefaultsAsync();

    /// <summary>
    /// 設定をバックアップします
    /// </summary>
    /// <param name="backupFilePath">バックアップファイルパス</param>
    /// <returns>バックアップタスク</returns>
    Task CreateBackupAsync(string? backupFilePath = null);

    /// <summary>
    /// バックアップから設定を復元します
    /// </summary>
    /// <param name="backupFilePath">バックアップファイルパス</param>
    /// <returns>復元タスク</returns>
    Task RestoreFromBackupAsync(string backupFilePath);

    #endregion

    #region 検証とマイグレーション

    /// <summary>
    /// 設定の妥当性を検証します
    /// </summary>
    /// <returns>検証結果</returns>
    SettingsValidationResult ValidateSettings();

    /// <summary>
    /// 設定のマイグレーションが必要かどうかを確認します
    /// </summary>
    /// <returns>マイグレーションが必要な場合はtrue</returns>
    bool RequiresMigration();

    /// <summary>
    /// 設定のマイグレーションを実行します
    /// </summary>
    /// <returns>マイグレーションタスク</returns>
    Task MigrateSettingsAsync();

    #endregion

    #region イベント

    /// <summary>
    /// 設定値が変更された時のイベント
    /// </summary>
    event EventHandler<SettingChangedEventArgs>? SettingChanged;

    /// <summary>
    /// ゲームプロファイルが変更された時のイベント
    /// </summary>
    event EventHandler<GameProfileChangedEventArgs>? GameProfileChanged;

    /// <summary>
    /// 設定の保存が完了した時のイベント
    /// </summary>
    event EventHandler<SettingsSavedEventArgs>? SettingsSaved;

    #endregion

    #region 統計・情報

    /// <summary>
    /// 設定の統計情報を取得します
    /// </summary>
    /// <returns>統計情報</returns>
    SettingsStatistics GetStatistics();

    /// <summary>
    /// 設定の変更履歴を取得します
    /// </summary>
    /// <param name="maxEntries">最大取得数</param>
    /// <returns>変更履歴</returns>
    IReadOnlyList<SettingChangeRecord> GetChangeHistory(int maxEntries = 100);

    /// <summary>
    /// お気に入り設定を追加します
    /// </summary>
    /// <param name="settingKey">設定キー</param>
    Task AddToFavoritesAsync(string settingKey);

    /// <summary>
    /// お気に入り設定を削除します
    /// </summary>
    /// <param name="settingKey">設定キー</param>
    Task RemoveFromFavoritesAsync(string settingKey);

    /// <summary>
    /// お気に入り設定を取得します
    /// </summary>
    /// <returns>お気に入り設定キーのリスト</returns>
    IReadOnlyList<string> GetFavoriteSettings();

    #endregion
}
