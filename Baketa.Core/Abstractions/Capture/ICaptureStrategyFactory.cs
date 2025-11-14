using Baketa.Core.Abstractions.GPU;
using Baketa.Core.Models.Capture;

namespace Baketa.Core.Abstractions.Capture;

/// <summary>
/// キャプチャ戦略ファクトリーのインターフェース
/// </summary>
public interface ICaptureStrategyFactory
{
    /// <summary>
    /// GPU環境に基づいて最適な戦略を取得
    /// </summary>
    ICaptureStrategy GetOptimalStrategy(GpuEnvironmentInfo environment, IntPtr hwnd);

    /// <summary>
    /// 利用可能なすべての戦略を優先順位順で取得
    /// </summary>
    IList<ICaptureStrategy> GetStrategiesInOrder(ICaptureStrategy? primaryStrategy = null);

    /// <summary>
    /// 特定の戦略タイプを取得
    /// </summary>
    ICaptureStrategy? GetStrategy(CaptureStrategyUsed strategyType);

    /// <summary>
    /// 利用可能な戦略タイプ一覧を取得
    /// </summary>
    IList<CaptureStrategyUsed> GetAvailableStrategyTypes();

    /// <summary>
    /// 戦略の実行条件を検証
    /// </summary>
    Task<bool> ValidateStrategyAsync(ICaptureStrategy strategy, GpuEnvironmentInfo environment, IntPtr hwnd);
}
