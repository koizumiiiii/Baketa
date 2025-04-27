using ReactiveUI.Validation.Contexts;
using ReactiveUI.Validation.Abstractions;

namespace Baketa.UI.Framework
{
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
}