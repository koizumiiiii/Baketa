namespace Baketa.Core.Settings;

/// <summary>
/// 投機的OCR設定
/// </summary>
/// <remarks>
/// Issue #293: 投機的実行とリソース適応
/// - リソース閾値を設定可能に（Geminiフィードバック反映）
/// - 省電力モードオプション追加
/// </remarks>
public sealed record SpeculativeOcrSettings
{
    /// <summary>
    /// 投機的OCRを有効にするかどうか
    /// </summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>
    /// 省電力モード（有効時は投機的OCRを無効化）
    /// </summary>
    /// <remarks>
    /// 低スペック環境や電力消費・発熱を抑えたい場合に使用
    /// </remarks>
    public bool EnablePowerSavingMode { get; init; } = false;

    #region リソース閾値

    /// <summary>
    /// GPU使用率閾値（%）- この値未満の場合に投機的OCRを実行
    /// </summary>
    /// <remarks>
    /// デフォルト: 50%
    /// ゲームプレイ中は常に超えている可能性があるため、調整可能にしている
    /// </remarks>
    public double GpuUsageThreshold { get; init; } = 50.0;

    /// <summary>
    /// VRAM使用率閾値（%）- この値未満の場合に投機的OCRを実行
    /// </summary>
    /// <remarks>
    /// デフォルト: 93%
    /// [Issue #293] 統合サーバー（NLLB + Surya OCR）がVRAMを大量消費するため、
    /// 70%では実質動作しない。93%に緩和してクラッシュしない範囲で動作させる。
    /// RTX 4070 12GBの場合、約11GB使用時に達する。
    /// Geminiレビュー: OOMリスクは1%緩和では低いと判断。
    /// </remarks>
    public double VramUsageThreshold { get; init; } = 93.0;

    /// <summary>
    /// 最小残りVRAM（MB）- この値以上の空きがある場合に投機的OCRを実行
    /// </summary>
    /// <remarks>
    /// デフォルト: 512MB
    /// [Issue #293] 統合サーバーがVRAMを大量消費するため2048MBでは厳しすぎる。
    /// Surya OCRの追加VRAM消費は数百MB程度のため、512MB確保できれば動作可能。
    /// パーセンテージ閾値に加えて絶対値でも制御可能（Geminiフィードバック反映）
    /// </remarks>
    public long MinAvailableVramMB { get; init; } = 512;

    /// <summary>
    /// CPU使用率閾値（%）- この値未満の場合に投機的OCRを実行
    /// </summary>
    /// <remarks>
    /// デフォルト: 80%
    /// CPU負荷が高い場合は投機的OCRを控える
    /// </remarks>
    public double CpuUsageThreshold { get; init; } = 80.0;

    #endregion

    #region キャッシュ設定

    /// <summary>
    /// キャッシュTTL（Time To Live）
    /// </summary>
    /// <remarks>
    /// デフォルト: 2秒
    /// ゲームジャンルによって調整が必要な場合がある：
    /// - RPG/ノベルゲーム: 5-10秒でも可
    /// - アクションゲーム: 1-2秒推奨
    /// </remarks>
    public TimeSpan CacheTtl { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// 画面変化検知を有効にするかどうか
    /// </summary>
    /// <remarks>
    /// 有効時: 画像ハッシュが変化したらキャッシュを即座に無効化
    /// 無効時: TTLのみでキャッシュ管理
    /// </remarks>
    public bool EnableScreenChangeDetection { get; init; } = true;

    #endregion

    #region 実行制御

    /// <summary>
    /// 投機的OCR実行の最小間隔
    /// </summary>
    /// <remarks>
    /// 連続実行を防ぐための最小待機時間
    /// デフォルト: 500ms
    /// </remarks>
    public TimeSpan MinExecutionInterval { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// 投機的OCR実行タイムアウト
    /// </summary>
    /// <remarks>
    /// OCR実行がこの時間を超えた場合はキャンセル
    /// デフォルト: 10秒
    /// </remarks>
    public TimeSpan ExecutionTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Live翻訳中は投機的OCRを無効にするかどうか
    /// </summary>
    /// <remarks>
    /// Live翻訳中はリソースを節約するため無効化推奨
    /// </remarks>
    public bool DisableDuringLiveTranslation { get; init; } = true;

    #endregion

    #region バックグラウンド学習設定（Phase 10）

    /// <summary>
    /// バックグラウンド学習を有効にするかどうか
    /// </summary>
    /// <remarks>
    /// Issue #293 Phase 10: 学習駆動型投機的OCR
    /// 有効時: ウィンドウ選択後、Live翻訳OFFでも定期的にキャプチャ・OCRを実行
    /// </remarks>
    public bool EnableBackgroundLearning { get; init; } = true;

    /// <summary>
    /// 学習完了と判定する高信頼度ROI領域の最小数
    /// </summary>
    /// <remarks>
    /// デフォルト: 3
    /// ビジュアルノベル等では1-2でも十分な場合があるため、プロファイル毎に調整可能
    /// </remarks>
    public int MinHighConfidenceRegions { get; init; } = 3;

    /// <summary>
    /// 学習完了と判定する最小総検出回数
    /// </summary>
    /// <remarks>
    /// デフォルト: 50
    /// 50回のOCR実行でヒートマップが十分に形成される想定
    /// </remarks>
    public int MinTotalDetections { get; init; } = 50;

    /// <summary>
    /// 学習完了と判定する最小ヒートマップカバー率（0.0-1.0）
    /// </summary>
    /// <remarks>
    /// デフォルト: 0.6（60%）
    /// ROI領域がテキスト検出領域の60%以上をカバーしていれば学習完了
    /// </remarks>
    public float MinHeatmapCoverage { get; init; } = 0.6f;

    /// <summary>
    /// 学習対象とするOCR信頼度の最小値（0.0-1.0）
    /// </summary>
    /// <remarks>
    /// デフォルト: 0.9（90%）
    /// Geminiフィードバック: 誤検出を防ぐため高信頼度のみ学習
    /// </remarks>
    public float MinOcrConfidenceForLearning { get; init; } = 0.9f;

    /// <summary>
    /// 学習対象とするテキストの最小文字数
    /// </summary>
    /// <remarks>
    /// [Issue #293] ノイズ検出を除外するためのフィルタ。
    /// 空文字列や1文字の検出（OCRアーティファクト）を除外します。
    /// デフォルト: 2（2文字以上のテキストのみ学習対象）
    /// </remarks>
    public int MinTextLengthForLearning { get; init; } = 2;

    /// <summary>
    /// 学習対象とする領域の最小幅（ピクセル）
    /// </summary>
    /// <remarks>
    /// [Issue #293] 極小のノイズ領域を除外するためのフィルタ。
    /// OCRが誤検出した微小な領域（数ピクセル程度）を除外します。
    /// デフォルト: 20ピクセル
    /// </remarks>
    public int MinRegionWidthForLearning { get; init; } = 20;

    /// <summary>
    /// 学習対象とする領域の最小高さ（ピクセル）
    /// </summary>
    /// <remarks>
    /// [Issue #293] 極小のノイズ領域を除外するためのフィルタ。
    /// OCRが誤検出した微小な領域（数ピクセル程度）を除外します。
    /// デフォルト: 10ピクセル
    /// </remarks>
    public int MinRegionHeightForLearning { get; init; } = 10;

    /// <summary>
    /// バックグラウンド実行をスキップするGPU使用率閾値（%）
    /// </summary>
    /// <remarks>
    /// デフォルト: 85%
    /// 通常の投機的OCR閾値（50%）より高め。ゲームへの影響を最小化。
    /// </remarks>
    public double GpuUsageThresholdForSkip { get; init; } = 85.0;

    /// <summary>
    /// 初期フェーズの実行間隔
    /// </summary>
    public TimeSpan InitialPhaseInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 学習中フェーズの実行間隔
    /// </summary>
    public TimeSpan LearningPhaseInterval { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// 維持モードフェーズの実行間隔
    /// </summary>
    public TimeSpan MaintenancePhaseInterval { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// カバー率計算: 高信頼度領域1つあたりの寄与率
    /// </summary>
    /// <remarks>
    /// デフォルト: 0.2（高信頼度領域5つで100%に達する）
    /// カバー率 = RegionCount × RegionContribution + DetectionCount × DetectionContribution
    /// </remarks>
    public float CoverageRegionContribution { get; init; } = 0.2f;

    /// <summary>
    /// カバー率計算: 検出1回あたりの寄与率
    /// </summary>
    /// <remarks>
    /// デフォルト: 0.01（100回検出で100%に達する）
    /// </remarks>
    public float CoverageDetectionContribution { get; init; } = 0.01f;

    /// <summary>
    /// バックグラウンド学習サービスの起動待機時間
    /// </summary>
    /// <remarks>
    /// アプリケーション起動完了を待つための初期待機時間
    /// デフォルト: 5秒
    /// </remarks>
    public TimeSpan BackgroundLearningStartupDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// バックグラウンド学習サービスのエラー時リトライ待機時間
    /// </summary>
    /// <remarks>
    /// 学習サイクルでエラー発生時の待機時間
    /// デフォルト: 10秒
    /// </remarks>
    public TimeSpan BackgroundLearningErrorRetryDelay { get; init; } = TimeSpan.FromSeconds(10);

    #endregion

    #region メトリクス

    /// <summary>
    /// メトリクス収集を有効にするかどうか
    /// </summary>
    public bool EnableMetricsCollection { get; init; } = true;

    /// <summary>
    /// 詳細ログを有効にするかどうか
    /// </summary>
    public bool EnableDetailedLogging { get; init; } = false;

    #endregion

    #region 検証

    /// <summary>
    /// 設定値の妥当性を検証
    /// </summary>
    public bool IsValid()
    {
        return GpuUsageThreshold is >= 0 and <= 100 &&
               VramUsageThreshold is >= 0 and <= 100 &&
               CpuUsageThreshold is >= 0 and <= 100 &&
               MinAvailableVramMB >= 0 &&
               CacheTtl > TimeSpan.Zero &&
               MinExecutionInterval >= TimeSpan.Zero &&
               ExecutionTimeout > TimeSpan.Zero &&
               // バックグラウンド学習設定の検証
               MinHighConfidenceRegions >= 1 &&
               MinTotalDetections >= 1 &&
               MinHeatmapCoverage is >= 0.0f and <= 1.0f &&
               MinOcrConfidenceForLearning is >= 0.0f and <= 1.0f &&
               MinTextLengthForLearning >= 0 &&
               MinRegionWidthForLearning >= 0 &&
               MinRegionHeightForLearning >= 0 &&
               GpuUsageThresholdForSkip is >= 0 and <= 100 &&
               InitialPhaseInterval > TimeSpan.Zero &&
               LearningPhaseInterval > TimeSpan.Zero &&
               MaintenancePhaseInterval > TimeSpan.Zero &&
               CoverageRegionContribution is >= 0.0f and <= 1.0f &&
               CoverageDetectionContribution is >= 0.0f and <= 1.0f &&
               BackgroundLearningStartupDelay >= TimeSpan.Zero &&
               BackgroundLearningErrorRetryDelay > TimeSpan.Zero;
    }

    /// <summary>
    /// 投機的OCRが実行可能かどうかを判定
    /// </summary>
    /// <param name="gpuUsagePercent">現在のGPU使用率</param>
    /// <param name="vramUsagePercent">現在のVRAM使用率</param>
    /// <param name="availableVramMB">残りVRAM (MB)</param>
    /// <param name="cpuUsagePercent">現在のCPU使用率</param>
    /// <returns>実行可能な場合はtrue</returns>
    public bool CanExecute(
        double gpuUsagePercent,
        double vramUsagePercent,
        long availableVramMB,
        double cpuUsagePercent)
    {
        if (!IsEnabled || EnablePowerSavingMode)
            return false;

        return gpuUsagePercent < GpuUsageThreshold &&
               vramUsagePercent < VramUsageThreshold &&
               availableVramMB >= MinAvailableVramMB &&
               cpuUsagePercent < CpuUsageThreshold;
    }

    #endregion
}
