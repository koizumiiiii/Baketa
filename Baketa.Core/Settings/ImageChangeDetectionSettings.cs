using System;

namespace Baketa.Core.Settings;

/// <summary>
/// 画像変化検知システムの設定
/// </summary>
/// <remarks>
/// P0 画像変化検知システムの3段階フィルタリングパイプライン設定を管理します。
/// LoggingSettingsパターンに準拠した実装で、appsettings.json:ImageChangeDetectionセクションから設定値を読み込みます。
/// </remarks>
public sealed record ImageChangeDetectionSettings
{
    /// <summary>
    /// Stage 1 類似度閾値（ハミング距離ベース高速フィルタリング）
    /// </summary>
    /// <remarks>
    /// デフォルト: 0.92（類似度が0.92未満の場合に変化ありとして次段階へ）
    /// 推奨範囲: 0.85-0.95
    /// </remarks>
    public float Stage1SimilarityThreshold { get; init; } = 0.92f;

    /// <summary>
    /// Stage 2 変化率閾値（中精度ハミング距離検証）
    /// </summary>
    /// <remarks>
    /// デフォルト: 0.05（5%以上の変化で変化ありとして判定）
    /// 推奨範囲: 0.03-0.08
    /// </remarks>
    public float Stage2ChangePercentageThreshold { get; init; } = 0.05f;

    /// <summary>
    /// Stage 3 SSIM閾値（高精度構造的類似性解析）
    /// </summary>
    /// <remarks>
    /// デフォルト: 0.92（SSIM値が0.92未満の場合に変化ありと判定）
    /// 推奨範囲: 0.90-0.95
    /// </remarks>
    public float Stage3SSIMThreshold { get; init; } = 0.92f;

    /// <summary>
    /// 領域別SSIM閾値（ROI変化検知用）
    /// </summary>
    /// <remarks>
    /// デフォルト: 0.95（領域固有の高精度検知）
    /// 推奨範囲: 0.92-0.98
    /// </remarks>
    public float RegionSSIMThreshold { get; init; } = 0.95f;

    /// <summary>
    /// キャッシング有効フラグ
    /// </summary>
    public bool EnableCaching { get; init; } = true;

    /// <summary>
    /// 最大キャッシュサイズ
    /// </summary>
    public int MaxCacheSize { get; init; } = 1000;

    /// <summary>
    /// キャッシュ有効期限（分）
    /// </summary>
    public int CacheExpirationMinutes { get; init; } = 30;

    /// <summary>
    /// パフォーマンスロギング有効フラグ
    /// </summary>
    public bool EnablePerformanceLogging { get; init; } = true;

    // ========================================
    // [Issue #229] グリッド分割ハッシュ設定
    // ========================================

    /// <summary>
    /// [Issue #229] グリッド分割ハッシュを有効化
    /// </summary>
    /// <remarks>
    /// 有効にすると、画面全体のハッシュ比較から
    /// グリッド分割による局所変化検知に切り替わります。
    /// テキスト変更のような小さな変化の検出精度が向上します。
    /// </remarks>
    public bool EnableGridPartitioning { get; init; } = true;

    /// <summary>
    /// [Issue #229] グリッドの行数
    /// </summary>
    /// <remarks>
    /// デフォルト: 4（4x4=16ブロック）
    /// 推奨範囲: 3-6
    /// </remarks>
    public int GridRows { get; init; } = 4;

    /// <summary>
    /// [Issue #229] グリッドの列数
    /// </summary>
    /// <remarks>
    /// デフォルト: 4（4x4=16ブロック）
    /// 推奨範囲: 3-6
    /// </remarks>
    public int GridColumns { get; init; } = 4;

    /// <summary>
    /// [Issue #229] グリッドブロック単位の類似度閾値
    /// </summary>
    /// <remarks>
    /// いずれか1ブロックでもこの閾値を下回れば「変化あり」と判定。
    /// デフォルト: 0.98（テキスト変更検知に最適化）
    /// 推奨範囲: 0.92-0.99
    /// Stage 1は高速フィルタ、Stage 2でノイズ除外するため、高感度設定が可能。
    /// テキスト変更は約1-5%の変化（類似度0.95-0.99程度）を生じる。
    /// </remarks>
    public float GridBlockSimilarityThreshold { get; init; } = 0.98f;

    // ========================================
    // [Issue #229] テキスト安定化待機設定
    // ========================================

    /// <summary>
    /// [Issue #229] テキスト安定化待機を有効化
    /// </summary>
    /// <remarks>
    /// 有効にすると、テキストが一文字ずつ表示されるアニメーション
    /// （タイプライター効果）に対応し、テキストが安定するまで
    /// OCR実行を遅延させます。
    /// </remarks>
    public bool EnableTextStabilization { get; init; } = true;

    /// <summary>
    /// [Issue #229] テキスト安定化待機時間（ミリ秒）
    /// </summary>
    /// <remarks>
    /// 最初の変化検知から、この時間変化がなければ「安定」と判定。
    /// デフォルト: 500ms（一般的なテキストアニメーション完了時間）
    /// 推奨範囲: 300-1000ms
    /// </remarks>
    public int TextStabilizationDelayMs { get; init; } = 500;

    /// <summary>
    /// [Issue #229] 安定化待機中の最大待機時間（ミリ秒）
    /// </summary>
    /// <remarks>
    /// 安定化待機が無限に続かないよう上限を設定。
    /// デフォルト: 3000ms（3秒でタイムアウト）
    /// </remarks>
    public int MaxStabilizationWaitMs { get; init; } = 3000;

    /// <summary>
    /// 設定値の妥当性を検証
    /// </summary>
    public bool IsValid()
    {
        return Stage1SimilarityThreshold is >= 0.0f and <= 1.0f
            && Stage2ChangePercentageThreshold is >= 0.0f and <= 1.0f
            && Stage3SSIMThreshold is >= 0.0f and <= 1.0f
            && RegionSSIMThreshold is >= 0.0f and <= 1.0f
            && MaxCacheSize > 0
            && CacheExpirationMinutes > 0
            // [Issue #229] グリッド分割設定の検証
            && GridRows > 0
            && GridColumns > 0
            && GridBlockSimilarityThreshold is > 0.0f and <= 1.0f
            // [Issue #229] テキスト安定化設定の検証
            && TextStabilizationDelayMs >= 0
            && MaxStabilizationWaitMs >= TextStabilizationDelayMs;
    }

    /// <summary>
    /// 開発/テスト用設定を作成
    /// </summary>
    public static ImageChangeDetectionSettings CreateDevelopmentSettings()
    {
        return new ImageChangeDetectionSettings
        {
            Stage1SimilarityThreshold = 0.92f,
            Stage2ChangePercentageThreshold = 0.05f,
            Stage3SSIMThreshold = 0.92f,
            RegionSSIMThreshold = 0.95f,
            EnableCaching = true,
            MaxCacheSize = 500,
            CacheExpirationMinutes = 15,
            EnablePerformanceLogging = true,
            // [Issue #229] テキスト安定化設定
            EnableTextStabilization = true,
            TextStabilizationDelayMs = 500,
            MaxStabilizationWaitMs = 3000
        };
    }

    /// <summary>
    /// 高感度設定を作成（変化を検知しやすい）
    /// </summary>
    public static ImageChangeDetectionSettings CreateHighSensitivitySettings()
    {
        return new ImageChangeDetectionSettings
        {
            Stage1SimilarityThreshold = 0.88f, // より低い閾値で変化を検知
            Stage2ChangePercentageThreshold = 0.03f, // 3%の変化で検知
            Stage3SSIMThreshold = 0.90f,
            RegionSSIMThreshold = 0.92f,
            EnableCaching = true,
            MaxCacheSize = 1000,
            CacheExpirationMinutes = 30,
            EnablePerformanceLogging = true,
            // [Issue #229] テキスト安定化設定（高感度: 短め待機）
            EnableTextStabilization = true,
            TextStabilizationDelayMs = 300,
            MaxStabilizationWaitMs = 2000
        };
    }

    /// <summary>
    /// 低感度設定を作成（ノイズに強い）
    /// </summary>
    public static ImageChangeDetectionSettings CreateLowSensitivitySettings()
    {
        return new ImageChangeDetectionSettings
        {
            Stage1SimilarityThreshold = 0.95f, // より高い閾値でノイズ除去
            Stage2ChangePercentageThreshold = 0.08f, // 8%の変化で検知
            Stage3SSIMThreshold = 0.95f,
            RegionSSIMThreshold = 0.97f,
            EnableCaching = true,
            MaxCacheSize = 1500,
            CacheExpirationMinutes = 60,
            EnablePerformanceLogging = true,
            // [Issue #229] テキスト安定化設定（低感度: 長め待機）
            EnableTextStabilization = true,
            TextStabilizationDelayMs = 800,
            MaxStabilizationWaitMs = 5000
        };
    }

    /// <summary>
    /// 本番環境用設定を作成
    /// </summary>
    public static ImageChangeDetectionSettings CreateProductionSettings()
    {
        return new ImageChangeDetectionSettings
        {
            Stage1SimilarityThreshold = 0.92f,
            Stage2ChangePercentageThreshold = 0.05f,
            Stage3SSIMThreshold = 0.92f,
            RegionSSIMThreshold = 0.95f,
            EnableCaching = true,
            MaxCacheSize = 2000,
            CacheExpirationMinutes = 60,
            EnablePerformanceLogging = false, // 本番ではパフォーマンスログを無効化
            // [Issue #229] テキスト安定化設定
            EnableTextStabilization = true,
            TextStabilizationDelayMs = 500,
            MaxStabilizationWaitMs = 3000
        };
    }
}
