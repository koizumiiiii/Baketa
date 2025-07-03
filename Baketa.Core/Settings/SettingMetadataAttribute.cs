using System;

namespace Baketa.Core.Settings;

/// <summary>
/// 設定項目メタデータ属性
/// 設定項目の表示情報、分類、制約などを定義
/// </summary>
/// <remarks>
/// SettingMetadataAttributeを初期化します
/// </remarks>
/// <param name="level">設定レベル</param>
/// <param name="category">カテゴリ名</param>
/// <param name="displayName">表示名</param>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class SettingMetadataAttribute(SettingLevel level, string category, string displayName) : Attribute
{
    /// <summary>
    /// 設定レベル（基本/詳細/デバッグ）
    /// </summary>
    public SettingLevel Level { get; } = level;

    /// <summary>
    /// カテゴリ名（設定グループ）
    /// </summary>
    public string Category { get; } = category ?? throw new ArgumentNullException(nameof(category));

    /// <summary>
    /// 表示名（UIに表示される名前）
    /// </summary>
    public string DisplayName { get; } = displayName ?? throw new ArgumentNullException(nameof(displayName));

    /// <summary>
    /// 説明文（ヘルプテキスト）
    /// </summary>
    public string Description { get; set; } = string.Empty;
    
    /// <summary>
    /// 再起動が必要かどうか
    /// </summary>
    public bool RequiresRestart { get; set; }
    
    /// <summary>
    /// ヘルプURL（詳細ドキュメントへのリンク）
    /// </summary>
    public string? HelpUrl { get; set; }
    
    /// <summary>
    /// 表示順序（小さい値ほど上に表示）
    /// </summary>
    public int DisplayOrder { get; set; }
    
    /// <summary>
    /// 最小値（数値型の場合）
    /// </summary>
    public object? MinValue { get; set; }
    
    /// <summary>
    /// 最大値（数値型の場合）
    /// </summary>
    public object? MaxValue { get; set; }
    
    /// <summary>
    /// 有効な値のリスト（選択肢型の場合）
    /// </summary>
    public object[]? ValidValues { get; set; }
    
    /// <summary>
    /// 単位文字列（数値型の場合）
    /// </summary>
    public string? Unit { get; set; }
    
    /// <summary>
    /// 設定変更時の警告メッセージ
    /// </summary>
    public string? WarningMessage { get; set; }
}
