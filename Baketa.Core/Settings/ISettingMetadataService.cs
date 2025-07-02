using System;
using System.Collections.Generic;

namespace Baketa.Core.Settings;

/// <summary>
/// 設定メタデータ管理サービスインターフェース
/// 設定項目のメタデータを管理し、動的UI生成をサポート
/// </summary>
public interface ISettingMetadataService
{
    /// <summary>
    /// 指定された設定クラスからメタデータを取得します
    /// </summary>
    /// <typeparam name="T">設定クラスの型</typeparam>
    /// <returns>メタデータのコレクション</returns>
    IReadOnlyList<SettingMetadata> GetMetadata<T>() where T : class;
    
    /// <summary>
    /// 指定された型からメタデータを取得します
    /// </summary>
    /// <param name="settingsType">設定クラスの型</param>
    /// <returns>メタデータのコレクション</returns>
    IReadOnlyList<SettingMetadata> GetMetadata(Type settingsType);
    
    /// <summary>
    /// 指定されたレベルのメタデータのみを取得します
    /// </summary>
    /// <typeparam name="T">設定クラスの型</typeparam>
    /// <param name="level">取得する設定レベル</param>
    /// <returns>フィルタリングされたメタデータのコレクション</returns>
    IReadOnlyList<SettingMetadata> GetMetadataByLevel<T>(SettingLevel level) where T : class;
    
    /// <summary>
    /// 指定されたカテゴリのメタデータのみを取得します
    /// </summary>
    /// <typeparam name="T">設定クラスの型</typeparam>
    /// <param name="category">取得するカテゴリ</param>
    /// <returns>フィルタリングされたメタデータのコレクション</returns>
    IReadOnlyList<SettingMetadata> GetMetadataByCategory<T>(string category) where T : class;
    
    /// <summary>
    /// 利用可能なカテゴリの一覧を取得します
    /// </summary>
    /// <typeparam name="T">設定クラスの型</typeparam>
    /// <returns>カテゴリ名のコレクション</returns>
    IReadOnlyList<string> GetCategories<T>() where T : class;
    
    /// <summary>
    /// 設定値の検証を実行します
    /// </summary>
    /// <param name="metadata">メタデータ</param>
    /// <param name="value">検証する値</param>
    /// <returns>検証結果</returns>
    SettingValidationResult ValidateValue(SettingMetadata metadata, object? value);
    
    /// <summary>
    /// 設定オブジェクト全体の検証を実行します
    /// </summary>
    /// <typeparam name="T">設定クラスの型</typeparam>
    /// <param name="settings">検証する設定オブジェクト</param>
    /// <returns>検証結果のコレクション</returns>
    IReadOnlyList<SettingValidationResult> ValidateSettings<T>(T settings) where T : class;
}
