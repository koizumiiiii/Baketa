using Baketa.Core.Models.Hardware;

namespace Baketa.Core.Abstractions.Hardware;

/// <summary>
/// [Issue #335] ハードウェアスペックチェッカーのインターフェース
/// </summary>
public interface IHardwareChecker
{
    /// <summary>
    /// ハードウェアスペックをチェック
    /// </summary>
    /// <returns>チェック結果</returns>
    HardwareCheckResult Check();
}
