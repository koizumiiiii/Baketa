using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Logging;

namespace Baketa.Core.Settings;

/// <summary>
/// 設定メタデータ管理サービス実装
/// リフレクションを使用して設定クラスからメタデータを抽出・管理
/// </summary>
public sealed class SettingMetadataService : ISettingMetadataService
{
    private readonly ILogger<SettingMetadataService> _logger;
    private readonly ConcurrentDictionary<Type, IReadOnlyList<SettingMetadata>> _metadataCache = new();

    /// <summary>
    /// SettingMetadataServiceを初期化します
    /// </summary>
    /// <param name="logger">ロガー</param>
    public SettingMetadataService(ILogger<SettingMetadataService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public IReadOnlyList<SettingMetadata> GetMetadata<T>() where T : class
    {
        return GetMetadata(typeof(T));
    }

    /// <inheritdoc />
    public IReadOnlyList<SettingMetadata> GetMetadata(Type settingsType)
    {
        ArgumentNullException.ThrowIfNull(settingsType);
        
        return _metadataCache.GetOrAdd(settingsType, type =>
        {
            try
            {
                var metadata = ExtractMetadata(type);
                _logger.LogDebug("設定メタデータを抽出しました: {Type}, {Count}項目", type.Name, metadata.Count);
                return metadata;
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
            {
                _logger.LogError(ex, "設定メタデータの抽出に失敗しました: {Type}", type.Name);
                return [];
            }
        });
    }

    /// <inheritdoc />
    public IReadOnlyList<SettingMetadata> GetMetadataByLevel<T>(SettingLevel level) where T : class
    {
        var allMetadata = GetMetadata<T>();
        return [.. allMetadata.Where(m => m.Level == level).OrderBy(m => m.DisplayOrder).ThenBy(m => m.DisplayName)];
    }

    /// <inheritdoc />
    public IReadOnlyList<SettingMetadata> GetMetadataByCategory<T>(string category) where T : class
    {
        // null値には ArgumentNullException を使用（.NET 慣例）
        ArgumentNullException.ThrowIfNull(category);
        
        // 空文字列には ArgumentException を使用
        if (string.IsNullOrEmpty(category))
        {
            throw new ArgumentException("カテゴリは必須です", nameof(category));
        }
        
        var allMetadata = GetMetadata<T>();
        return [.. allMetadata.Where(m => string.Equals(m.Category, category, StringComparison.OrdinalIgnoreCase))
                         .OrderBy(m => m.DisplayOrder)
                         .ThenBy(m => m.DisplayName)];
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetCategories<T>() where T : class
    {
        var allMetadata = GetMetadata<T>();
        return [.. allMetadata.Select(m => m.Category).Distinct().OrderBy(c => c)];
    }

    /// <inheritdoc />
    public SettingValidationResult ValidateValue(SettingMetadata metadata, object? value)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        
        try
        {
            // 基本的な値検証
            if (!metadata.IsValidValue(value))
            {
                var errorMessage = CreateValidationErrorMessage(metadata, value);
                return SettingValidationResult.Failure(metadata, value, errorMessage);
            }
            
            // 警告メッセージの確認
            var warningMessage = metadata.WarningMessage;
            
            return SettingValidationResult.Success(metadata, value, warningMessage);
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            _logger.LogError(ex, "設定値の検証中にエラーが発生しました: {PropertyName}", metadata.Property.Name);
            return SettingValidationResult.Failure(metadata, value, $"検証エラー: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<SettingValidationResult> ValidateSettings<T>(T settings) where T : class
    {
        ArgumentNullException.ThrowIfNull(settings);
        
        var metadata = GetMetadata<T>();
        var results = new List<SettingValidationResult>();
        
        foreach (var meta in metadata)
        {
            try
            {
                var value = meta.GetValue(settings);
                var result = ValidateValue(meta, value);
                results.Add(result);
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
            {
                _logger.LogError(ex, "設定プロパティの検証中にエラーが発生しました: {PropertyName}", meta.Property.Name);
                results.Add(SettingValidationResult.Failure(meta, null, $"プロパティアクセスエラー: {ex.Message}"));
            }
        }
        
        return results;
    }

    /// <summary>
    /// 指定された型からメタデータを抽出します
    /// </summary>
    private static IReadOnlyList<SettingMetadata> ExtractMetadata(Type type)
    {
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var metadataList = new List<SettingMetadata>();
        
        foreach (var property in properties)
        {
            var attribute = property.GetCustomAttribute<SettingMetadataAttribute>();
            if (attribute != null)
            {
                var metadata = new SettingMetadata(property, attribute);
                metadataList.Add(metadata);
            }
        }
        
        // 表示順序とカテゴリ、名前でソート
        return [.. metadataList.OrderBy(m => m.Category)
                          .ThenBy(m => m.DisplayOrder)
                          .ThenBy(m => m.DisplayName)];
    }
    
    /// <summary>
    /// 検証エラーメッセージを作成します
    /// </summary>
    private static string CreateValidationErrorMessage(SettingMetadata metadata, object? value)
    {
        if (value == null)
        {
            return $"{metadata.DisplayName}は必須項目です";
        }
        
        // 型チェック
        if (!metadata.PropertyType.IsAssignableFrom(value.GetType()))
        {
            return $"{metadata.DisplayName}の型が正しくありません。期待される型: {metadata.PropertyType.Name}";
        }
        
        // 範囲チェック
        if (metadata.MinValue != null || metadata.MaxValue != null)
        {
            if (value is IComparable comparableValue)
            {
                if (metadata.MinValue != null && comparableValue.CompareTo(metadata.MinValue) < 0)
                {
                    var unit = !string.IsNullOrEmpty(metadata.Unit) ? metadata.Unit : "";
                    return $"{metadata.DisplayName}は{metadata.MinValue}{unit}以上である必要があります";
                }
                
                if (metadata.MaxValue != null && comparableValue.CompareTo(metadata.MaxValue) > 0)
                {
                    var unit = !string.IsNullOrEmpty(metadata.Unit) ? metadata.Unit : "";
                    return $"{metadata.DisplayName}は{metadata.MaxValue}{unit}以下である必要があります";
                }
            }
        }
        
        // 選択肢チェック
        if (metadata.ValidValues != null && metadata.ValidValues.Length > 0)
        {
            var validValuesStr = string.Join(", ", metadata.ValidValues);
            return $"{metadata.DisplayName}は有効な値を選択してください。有効な値: {validValuesStr}";
        }
        
        return $"{metadata.DisplayName}の値が無効です";
    }
}
