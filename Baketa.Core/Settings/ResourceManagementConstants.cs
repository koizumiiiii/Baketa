using System;

namespace Baketa.Core.Settings;

/// <summary>
/// リソース管理関連の定数値
/// マジックナンバー撲滅のための設定値集約クラス
/// </summary>
public static class ResourceManagementConstants
{
    /// <summary>
    /// VRAM関連定数
    /// </summary>
    public static class Vram
    {
        /// <summary>デフォルトフォールバックVRAM容量（MB）</summary>
        public const long DefaultCapacityMB = 8192; // 8GB

        /// <summary>一般的なVRAMサイズ（MB）</summary>
        public static readonly long[] CommonCapacityMB = 
        [
            4096,   // 4GB
            6144,   // 6GB  
            8192,   // 8GB
            10240,  // 10GB
            12288,  // 12GB
            16384,  // 16GB
            20480,  // 20GB
            24576   // 24GB
        ];

        /// <summary>VRAM使用率計算時の許容範囲（％）</summary>
        public const double MinUsagePercentForEstimation = 10.0; // 10%
        public const double MaxUsagePercentForEstimation = 90.0; // 90%

        /// <summary>VRAM容量キャッシュ有効期間（分）</summary>
        public static readonly TimeSpan CacheValidityDuration = TimeSpan.FromMinutes(30);
    }

    /// <summary>
    /// 統計的分析関連定数
    /// </summary>
    public static class Statistics
    {
        /// <summary>統計的有意性検定の閾値（α）</summary>
        public const double SignificanceThreshold = 0.05; // α = 0.05

        /// <summary>最小サンプルサイズ（中心極限定理）</summary>
        public const int MinimumSampleSize = 30;

        /// <summary>効果量分類閾値</summary>
        public const double SmallEffectSize = 0.2;
        public const double MediumEffectSize = 0.5;
        public const double LargeEffectSize = 0.8;

        /// <summary>Yatesの連続性補正値</summary>
        public const double YatesCorrectionValue = 0.5;

        /// <summary>分散推定時の標準偏差係数（平均の30%）</summary>
        public const double StandardDeviationCoefficient = 0.3;
    }

    /// <summary>
    /// パフォーマンス管理関連定数
    /// </summary>
    public static class Performance
    {
        /// <summary>OCRチャンネル容量</summary>
        public const int DefaultOcrChannelCapacity = 100;

        /// <summary>サンプリング間隔（ミリ秒）</summary>
        public const int DefaultSamplingIntervalMs = 1000;

        /// <summary>履歴保持件数制限</summary>
        public const int MaxHistoryCount = 1000;
        public const int MaxAccuracyHistoryCount = 100;
        public const int MaxRecentHistoryMinutes = 10;

        /// <summary>負荷計算時の重み係数</summary>
        public const double CpuLoadWeight = 0.3;
        public const double MemoryLoadWeight = 0.2;
        public const double GpuLoadWeight = 0.25;
        public const double VramLoadWeight = 0.25;
    }

    /// <summary>
    /// タイミング関連定数
    /// </summary>
    public static class Timing
    {
        /// <summary>デフォルト待機時間（ミリ秒）</summary>
        public const int DefaultDelayMs = 100;
        public const int ShortDelayMs = 50;
        public const int MediumDelayMs = 500;

        /// <summary>タイムアウト時間</summary>
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan ShortTimeout = TimeSpan.FromSeconds(10);

        /// <summary>評価・チェック間隔</summary>
        public static readonly TimeSpan PerformanceEvaluationInterval = TimeSpan.FromMinutes(5);
        public static readonly TimeSpan AbTestEvaluationInterval = TimeSpan.FromMinutes(10);
        public static readonly TimeSpan PredictedPeakTime = TimeSpan.FromMinutes(10);

        /// <summary>冷却時間制限</summary>
        public const int MinCooldownMs = 500;
        public const int MaxCooldownMs = 30000; // 30秒
        public const int BaseCooldownMs = 1000; // 1秒
        public const int CooldownScaleFactorMs = 4000; // スケールファクター
    }

    /// <summary>
    /// スケーリング・調整係数
    /// </summary>
    public static class Scaling
    {
        /// <summary>負荷カーブ調整指数</summary>
        public const double LoadCurveExponent = 1.3;

        /// <summary>調整範囲制限</summary>
        public const double MinAdjustmentRatio = 0.5; // 0.5倍
        public const double MaxAdjustmentRatio = 2.0; // 2.0倍

        /// <summary>温度・VRAM圧迫度調整係数</summary>
        public const double StableGameCooldownMultiplier = 0.8;
        public const double TemperatureAdjustmentFactor = 0.3;
        public const double VramPressureAdjustmentFactor = 0.8;

        /// <summary>予測精度調整係数</summary>
        public const double PredictionAccuracyWeight = 0.5;
        public const double MinPredictionAccuracy = 0.8;

        /// <summary>精度履歴更新重み</summary>
        public const double CurrentAccuracyWeight = 0.8;
        public const double NewAccuracyWeight = 0.2;
    }

    /// <summary>
    /// 閾値関連定数
    /// </summary>
    public static class Thresholds
    {
        /// <summary>負荷レベル判定閾値（％）</summary>
        public const double LowLoadThreshold = 20.0;
        public const double ModerateLoadThreshold = 50.0;
        public const double HighLoadThreshold = 70.0;
        public const double CriticalLoadThreshold = 90.0;
        public const double EmergencyLoadThreshold = 100.0;

        /// <summary>CPU・GPU使用率調整閾値</summary>
        public const double CpuThresholdAdjustment = 10.0; // OCR操作時の調整値

        /// <summary>A/Bテスト最小測定回数</summary>
        public const int MinAbTestMeasurements = 10;

        /// <summary>統計分析最小件数</summary>
        public const int MinStatisticalAnalysisCount = 10;
        public const int StatisticalComparisonSampleCount = 20; // 新旧比較用
    }

    /// <summary>
    /// トラフィック分割・配分
    /// </summary>
    public static class Traffic
    {
        /// <summary>A/Bテストトラフィック分割比率</summary>
        public const double ConservativeVariantRatio = 0.3; // 30%
        public const double DefaultVariantRatio = 0.4;      // 40%  
        public const double AggressiveVariantRatio = 0.3;   // 30%

        /// <summary>統計的有意性評価閾値</summary>
        public const double DefaultStatisticalSignificanceThreshold = 0.05;
    }

    /// <summary>
    /// エラー・フォールバック値
    /// </summary>
    public static class Fallback
    {
        /// <summary>フォールバックVRAM容量情報</summary>
        public static readonly (long Total, long Used, long Available, double UsagePercent) 
            DefaultVramInfo = (8192, 0, 8192, 0.0);

        /// <summary>デフォルト統計値</summary>
        public const double DefaultPValue = 1.0;
        public const double DefaultEffectSize = 0.0;
        public const double DefaultConfidence = 0.0;

        /// <summary>緊急時動作設定</summary>
        public const double EmergencyCurrentLoad = 100.0;
        public static readonly TimeSpan EmergencyFallbackCooldown = TimeSpan.FromSeconds(30);
    }
}