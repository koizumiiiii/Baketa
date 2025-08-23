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
/// 完全なフレーズ認識を促進する最適化された設定（根本原因修正版）
/// </summary>
public class TextDetectionConfig
{
    public int MinTextWidth { get; set; } = 4;   // より小さなテキストも検出（8→4）
    public int MinTextHeight { get; set; } = 4;  // より小さなテキストも検出（6→4）
    public int MinTextArea { get; set; } = 16;   // より小さな領域も対象に（60→16、4×4の一貫性）
    public float MinAspectRatio { get; set; } = 0.02f;  // より幅広い形状に対応（0.05→0.02）
    public float MaxAspectRatio { get; set; } = 50.0f;  // 長いテキストライン対応（30→50）
    public int EdgeDetectionThreshold { get; set; } = 20;  // より低い閾値で広範囲検出（30→20）
    public int NoiseReductionLevel { get; set; } = 1;   // ノイズ除去を最小限に（2→1）
    public float MergeDistanceThreshold { get; set; } = 100.0f;  // より遠い領域も結合（70→100）
}