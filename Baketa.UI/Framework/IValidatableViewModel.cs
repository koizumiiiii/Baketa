using ReactiveUI.Validation.Abstractions;
using ReactiveUI.Validation.Contexts;

namespace Baketa.UI.Framework;

/// <summary>
/// バリデーション可能なビューモデルのインターフェース
/// </summary>
internal interface IValidatableViewModel
{
    /// <summary>
    /// バリデーションコンテキスト
    /// </summary>
    IValidationContext ValidationContext { get; }
}
