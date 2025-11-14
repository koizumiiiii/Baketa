using System;
using System.Reflection;

namespace Baketa.Core.Settings;

/// <summary>
/// 設定項目のメタデータ情報
/// リフレクションで取得したメタデータの構造化データ
/// </summary>
public sealed class SettingMetadata
{
    /// <summary>
    /// プロパティ情報
    /// </summary>
    public PropertyInfo Property { get; }

    /// <summary>
    /// 設定レベル
    /// </summary>
    public SettingLevel Level { get; }

    /// <summary>
    /// カテゴリ名
    /// </summary>
    public string Category { get; }

    /// <summary>
    /// 表示名
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// 説明文
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// 再起動が必要かどうか
    /// </summary>
    public bool RequiresRestart { get; }

    /// <summary>
    /// ヘルプURL
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1056:URI-like properties should not be strings",
        Justification = "設定値の柔軟性とメタデータ管理のためstringで保持")]
    public string? HelpUrl { get; }

    /// <summary>
    /// 表示順序
    /// </summary>
    public int DisplayOrder { get; }

    /// <summary>
    /// 最小値
    /// </summary>
    public object? MinValue { get; }

    /// <summary>
    /// 最大値
    /// </summary>
    public object? MaxValue { get; }

    /// <summary>
    /// 有効な値のリスト
    /// </summary>
    public object[]? ValidValues { get; }

    /// <summary>
    /// 単位文字列
    /// </summary>
    public string? Unit { get; }

    /// <summary>
    /// 警告メッセージ
    /// </summary>
    public string? WarningMessage { get; }

    /// <summary>
    /// プロパティのフルキー（"Category.PropertyName"形式）
    /// </summary>
    public string FullKey => $"{Category}.{Property.Name}";

    /// <summary>
    /// プロパティの型
    /// </summary>
    public Type PropertyType => Property.PropertyType;

    /// <summary>
    /// SettingMetadataを初期化します
    /// </summary>
    public SettingMetadata(PropertyInfo property, SettingMetadataAttribute attribute)
    {
        Property = property ?? throw new ArgumentNullException(nameof(property));
        ArgumentNullException.ThrowIfNull(attribute);

        Level = attribute.Level;
        Category = attribute.Category;
        DisplayName = attribute.DisplayName;
        Description = attribute.Description;
        RequiresRestart = attribute.RequiresRestart;
        HelpUrl = attribute.HelpUrl;
        DisplayOrder = attribute.DisplayOrder;
        MinValue = attribute.MinValue;
        MaxValue = attribute.MaxValue;
        ValidValues = attribute.ValidValues;
        Unit = attribute.Unit;
        WarningMessage = attribute.WarningMessage;
    }

    /// <summary>
    /// 指定したオブジェクトから設定値を取得します
    /// </summary>
    /// <param name="settings">設定オブジェクト</param>
    /// <returns>設定値</returns>
    public object? GetValue(object settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return Property.GetValue(settings);
    }

    /// <summary>
    /// 指定したオブジェクトに設定値を設定します
    /// </summary>
    /// <param name="settings">設定オブジェクト</param>
    /// <param name="value">設定値</param>
    public void SetValue(object settings, object? value)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Property.SetValue(settings, value);
    }

    /// <summary>
    /// 値が有効範囲内かどうかを検証します
    /// </summary>
    /// <param name="value">検証する値</param>
    /// <returns>有効な場合はtrue</returns>
    public bool IsValidValue(object? value)
    {
        if (value == null) return !PropertyType.IsValueType || Nullable.GetUnderlyingType(PropertyType) != null;

        // 型チェック
        if (!PropertyType.IsAssignableFrom(value.GetType()))
        {
            return false;
        }

        // 範囲チェック（数値型）
        if (MinValue != null || MaxValue != null)
        {
            if (value is IComparable comparableValue)
            {
                if (MinValue != null && comparableValue.CompareTo(MinValue) < 0) return false;
                if (MaxValue != null && comparableValue.CompareTo(MaxValue) > 0) return false;
            }
        }

        // 選択肢チェック
        if (ValidValues != null && ValidValues.Length > 0)
        {
            return Array.Exists(ValidValues, v => Equals(v, value));
        }

        return true;
    }
}
