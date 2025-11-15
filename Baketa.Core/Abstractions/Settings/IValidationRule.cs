using Baketa.Core.Settings;
using Baketa.Core.Settings.Validation;

namespace Baketa.Core.Abstractions.Settings;

/// <summary>
/// 検証ルールインターフェース
/// 個別の設定項目に対する検証ロジックを定義
/// </summary>
public interface IValidationRule
{
    /// <summary>
    /// ルールの対象となるプロパティパス
    /// </summary>
    string PropertyPath { get; }

    /// <summary>
    /// ルールの優先度（数値が小さいほど高優先度）
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// ルールの説明
    /// </summary>
    string Description { get; }

    /// <summary>
    /// 値を検証します
    /// </summary>
    /// <param name="value">検証する値</param>
    /// <param name="context">検証コンテキスト</param>
    /// <returns>検証結果</returns>
    SettingValidationResult Validate(object? value, ValidationContext context);

    /// <summary>
    /// このルールが指定されたプロパティパスに適用可能かどうかを判定します
    /// </summary>
    /// <param name="propertyPath">プロパティパス</param>
    /// <returns>適用可能な場合はtrue</returns>
    bool CanApplyTo(string propertyPath);
}
