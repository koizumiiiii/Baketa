using Baketa.Core.Abstractions.Platform.Windows;
using Baketa.Core.Abstractions.GPU;
using System.Drawing;

namespace Baketa.Core.Models.Capture;

/// <summary>
/// キャプチャ戦略の種類
/// </summary>
public enum CaptureStrategyUsed
{
    DirectFullScreen,       // 統合GPU：直接大画面キャプチャ
    ROIBased,              // 専用GPU：段階的ROIキャプチャ  
    PrintWindowFallback,   // ソフトウェア：確実動作保証
    GDIFallback            // 最終手段：古いシステム対応
}

/// <summary>
/// キャプチャオプション
/// </summary>
public class CaptureOptions
{
    public bool AllowDirectFullScreen { get; set; } = true;
    public bool AllowROIProcessing { get; set; } = true; 
    public bool AllowSoftwareFallback { get; set; } = true;
    public float ROIScaleFactor { get; set; } = 0.25f;
    public int MaxRetryAttempts { get; set; } = 3;
    public bool EnableHDRProcessing { get; set; } = true;
    public int TDRTimeoutMs { get; set; } = 2000;
}

/// <summary>
/// キャプチャメトリクス
/// </summary>
public class CaptureMetrics
{
    public TimeSpan TotalProcessingTime { get; set; }
    public TimeSpan GPUDetectionTime { get; set; }
    public TimeSpan StrategySelectionTime { get; set; }
    public TimeSpan ActualCaptureTime { get; set; }
    public TimeSpan TextureConversionTime { get; set; }
    
    public long MemoryUsedMB { get; set; }
    public int RetryAttempts { get; set; }
    public int FrameCount { get; set; }
    public string PerformanceCategory { get; set; } = string.Empty;
}

/// <summary>
/// 戦略実行結果
/// </summary>
public class CaptureStrategyResult
{
    public bool Success { get; set; }
    public IList<IWindowsImage> Images { get; set; } = [];
    public IList<Rectangle> TextRegions { get; set; } = [];
    public string StrategyName { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public CaptureMetrics Metrics { get; set; } = new CaptureMetrics();
    public DateTime CompletionTime { get; set; } = DateTime.Now;
}

/// <summary>
/// 適応的キャプチャ結果
/// </summary>
public class AdaptiveCaptureResult
{
    public bool Success { get; set; }
    public IList<IWindowsImage> CapturedImages { get; set; } = [];
    public CaptureStrategyUsed StrategyUsed { get; set; }
    public GpuEnvironmentInfo GpuEnvironment { get; set; } = new();
    public TimeSpan ProcessingTime { get; set; }
    public IList<string> FallbacksAttempted { get; set; } = [];
    public IList<Rectangle> DetectedTextRegions { get; set; } = [];
    public CaptureMetrics Metrics { get; set; } = new CaptureMetrics();
    public string ErrorDetails { get; set; } = string.Empty;
    public DateTime CaptureTime { get; set; } = DateTime.Now;
}

/// <summary>
/// テキスト検出設定
/// 完全なフレーズ認識を促進する最適化された設定
/// </summary>
public class TextDetectionConfig
{
    public int MinTextWidth { get; set; } = 8;   // より小さなテキストも検出
    public int MinTextHeight { get; set; } = 6;  // より小さなテキストも検出
    public int MinTextArea { get; set; } = 60;   // より小さな領域も対象に
    public float MinAspectRatio { get; set; } = 0.05f;  // より幅広い形状に対応
    public float MaxAspectRatio { get; set; } = 30.0f;  // 長いテキストライン対応
    public int EdgeDetectionThreshold { get; set; } = 30;  // より低い閾値で広範囲検出
    public int NoiseReductionLevel { get; set; } = 2;   // ノイズ除去を軽減してテキスト保持
    public float MergeDistanceThreshold { get; set; } = 70.0f;  // より遠い領域も結合
}