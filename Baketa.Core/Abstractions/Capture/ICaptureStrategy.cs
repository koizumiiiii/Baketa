using Baketa.Core.Models.Capture;
using Baketa.Core.Abstractions.GPU;

namespace Baketa.Core.Abstractions.Capture;

/// <summary>
/// キャプチャ戦略の抽象インターフェース
/// </summary>
public interface ICaptureStrategy
{
    /// <summary>
    /// 戦略名
    /// </summary>
    string StrategyName { get; }
    
    /// <summary>
    /// この戦略が適用可能かチェック
    /// </summary>
    bool CanApply(GpuEnvironmentInfo environment, IntPtr hwnd);
    
    /// <summary>
    /// キャプチャを実行
    /// </summary>
    Task<CaptureStrategyResult> ExecuteCaptureAsync(IntPtr hwnd, CaptureOptions options);
    
    /// <summary>
    /// 戦略の優先度（数値が高いほど優先される）
    /// </summary>
    int Priority { get; }
    
    /// <summary>
    /// この戦略に必要な事前条件をチェック
    /// </summary>
    Task<bool> ValidatePrerequisitesAsync(IntPtr hwnd);
}