using System.Drawing;
using Baketa.Core.Abstractions.GPU;
using Baketa.Core.Abstractions.Platform.Windows;

namespace Baketa.Core.Models.Capture;

/// <summary>
/// キャプチャ戦略の種類
/// </summary>
public enum CaptureStrategyUsed
{
    DirectFullScreen,       // 統合GPU：直接大画面キャプチャ
    FullScreenOcr,         // 🔥 [PHASE2] 全画面OCR直接翻訳方式：ROI廃止により60-80%高速化
    PrintWindowFallback,   // ソフトウェア：確実動作保証
    GDIFallback            // 最終手段：古いシステム対応
}

// 🔥 [PHASE_K-29-G] CaptureOptionsクラスを削除
// 理由: Baketa.Core.Abstractions.Services.ICaptureService.CaptureOptionsに統合済み
// 影響範囲: 20ファイルのusing文を更新する必要あり
// 移行先: using Baketa.Core.Abstractions.Services;

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

    /// <summary>
    /// 🚀 [Issue #193] キャプチャ段階で実行されたOCR結果（座標スケーリング済み）
    /// FullScreenOcrCaptureStrategyで設定され、下流のパイプラインで使用される
    /// </summary>
    public Baketa.Core.Abstractions.OCR.OcrResults? PreExecutedOcrResult { get; set; }
}

/// <summary>
/// 適応的キャプチャ結果
/// Phase 1: OCR処理最適化システム対応
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

    /// <summary>
    /// 画像変化検知によりOCR処理がスキップされたかどうか
    /// Phase 1: OCR処理最適化システム
    /// </summary>
    public bool ImageChangeSkipped { get; set; } = false;

    // 🔥 [PHASE5] ROIメタデータ削除 - ROI廃止により不要

    /// <summary>
    /// 後続処理を継続すべきかどうかを示すフラグ
    /// Phase 1: 重複チャンク検出対応（Option B - Gemini推奨DTO導入アプローチ）
    /// </summary>
    /// <remarks>
    /// 🎯 [P0-1_PHASE3_GEMINI_RECOMMENDED] 重複チャンク問題の根本解決
    /// - 従来方式の場合: true （通常通り後続処理を実行）
    /// - 従来方式の場合: true （通常通り後続処理を実行）
    ///
    /// **背景**: ROI方式では既にROIImageCapturedEventで全ROI画像を処理済み。
    /// PrimaryImageを返すと、TranslationOrchestrationServiceが従来フローを実行し、
    /// 同じROI #0が2回処理される（ChunkID: 2 と 1000002）。
    ///
    /// **効果**: このフラグでROI方式と従来方式を明確に区別し、重複処理を根本から防止。
    /// </remarks>
    public bool ShouldContinueProcessing { get; set; } = true;

    /// <summary>
    /// キャプチャ段階で既に実行されたOCR結果（あれば）
    /// Phase 2: FullScreenOcrCaptureStrategy対応
    /// </summary>
    /// <remarks>
    /// 🔥 [PHASE2.2] ROI廃止による全画面OCR直接実行対応
    /// - FullScreenOcrCaptureStrategyが使用された場合、OCR結果がキャプチャ時に取得される
    /// - nullでない場合、SmartProcessingPipelineServiceはOcrExecutionStageStrategyをスキップする
    /// - 従来のキャプチャ戦略（DirectFullScreen等）ではnullのまま、通常のOCR段階で処理される
    /// </remarks>
    public Baketa.Core.Abstractions.OCR.OcrResults? PreExecutedOcrResult { get; set; } = null;
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
