using System;
using System.Collections.Generic;
using Baketa.Core.Models.Roi;
using Baketa.Core.Settings;

namespace Baketa.Infrastructure.Roi.SeedProfiles;

/// <summary>
/// [Issue #369] シードプロファイルプロバイダー
/// </summary>
/// <remarks>
/// 新規ゲーム検出時に適用するデフォルトのROI領域を提供します。
/// 王道ビジュアルノベルレイアウトに基づいたテンプレートを含みます。
/// </remarks>
public static class SeedProfileProvider
{
    #region シード領域の座標定義

    /// <summary>
    /// メインダイアログ領域（画面下部）
    /// 大部分のVNでテキストが表示される領域
    /// </summary>
    private static readonly NormalizedRect VnMainDialogBounds = new(0.03f, 0.66f, 0.94f, 0.22f);

    /// <summary>
    /// 名前枠領域（ダイアログの左上）
    /// キャラクター名が表示される領域
    /// </summary>
    private static readonly NormalizedRect VnNameBoxBounds = new(0.03f, 0.58f, 0.25f, 0.08f);

    #endregion

    /// <summary>
    /// デフォルトVNプロファイルのシード領域を取得
    /// </summary>
    /// <param name="settings">ROIマネージャー設定</param>
    /// <returns>シード領域のリスト</returns>
    public static IReadOnlyList<RoiRegion> GetDefaultVnRegions(RoiManagerSettings settings)
    {
        var now = DateTime.UtcNow;
        var detectionCount = settings.SeedProfileInitialDetectionCount;
        var confidenceScore = settings.SeedProfileInitialConfidenceScore;

        return
        [
            // メインダイアログ領域
            new RoiRegion
            {
                Id = "seed-main-dialog",
                NormalizedBounds = VnMainDialogBounds,
                RegionType = RoiRegionType.DialogBox,
                ConfidenceLevel = RoiConfidenceLevel.Medium,
                ConfidenceScore = confidenceScore,
                DetectionCount = detectionCount,
                HeatmapValue = confidenceScore,
                LastDetectedAt = now,
                CreatedAt = now
            },
            // 名前枠領域
            new RoiRegion
            {
                Id = "seed-name-box",
                NormalizedBounds = VnNameBoxBounds,
                RegionType = RoiRegionType.Text,
                ConfidenceLevel = RoiConfidenceLevel.Medium,
                ConfidenceScore = confidenceScore * 0.9f, // メインダイアログより少し低め
                DetectionCount = detectionCount / 2,
                HeatmapValue = confidenceScore * 0.9f,
                LastDetectedAt = now,
                CreatedAt = now
            }
        ];
    }

    /// <summary>
    /// デフォルトVNプロファイルのシードヒートマップを生成
    /// </summary>
    /// <param name="settings">ROIマネージャー設定</param>
    /// <returns>シードヒートマップデータ</returns>
    public static RoiHeatmapData GetDefaultVnHeatmap(RoiManagerSettings settings)
    {
        var rows = settings.HeatmapRows;
        var columns = settings.HeatmapColumns;
        var heatmap = RoiHeatmapData.Create(rows, columns);

        // シード領域に対応するヒートマップセルに初期値を設定
        var seedValue = settings.SeedProfileInitialConfidenceScore;
        var values = (float[])heatmap.Values.Clone();
        var sampleCounts = (int[])heatmap.SampleCounts.Clone();
        var initialSamples = settings.SeedProfileInitialDetectionCount;

        // メインダイアログ領域をヒートマップに反映
        var dialogStartRow = (int)(VnMainDialogBounds.Y * rows);
        var dialogEndRow = (int)(VnMainDialogBounds.Bottom * rows);
        var dialogStartCol = (int)(VnMainDialogBounds.X * columns);
        var dialogEndCol = (int)(VnMainDialogBounds.Right * columns);

        for (var row = dialogStartRow; row <= dialogEndRow && row < rows; row++)
        {
            for (var col = dialogStartCol; col <= dialogEndCol && col < columns; col++)
            {
                var index = row * columns + col;
                values[index] = seedValue;
                sampleCounts[index] = initialSamples;
            }
        }

        // 名前枠領域をヒートマップに反映
        var nameStartRow = (int)(VnNameBoxBounds.Y * rows);
        var nameEndRow = (int)(VnNameBoxBounds.Bottom * rows);
        var nameStartCol = (int)(VnNameBoxBounds.X * columns);
        var nameEndCol = (int)(VnNameBoxBounds.Right * columns);

        for (var row = nameStartRow; row < nameEndRow && row < rows; row++)
        {
            for (var col = nameStartCol; col <= nameEndCol && col < columns; col++)
            {
                var index = row * columns + col;
                values[index] = seedValue * 0.9f;
                sampleCounts[index] = initialSamples / 2;
            }
        }

        return heatmap with
        {
            Values = values,
            SampleCounts = sampleCounts,
            LastUpdatedAt = DateTime.UtcNow,
            TotalSamples = initialSamples
        };
    }

    /// <summary>
    /// シードプロファイルを作成
    /// </summary>
    /// <param name="profileId">プロファイルID</param>
    /// <param name="name">プロファイル名</param>
    /// <param name="executablePath">実行ファイルパス</param>
    /// <param name="windowTitle">ウィンドウタイトル</param>
    /// <param name="settings">ROIマネージャー設定</param>
    /// <returns>シードデータが設定されたプロファイル</returns>
    public static RoiProfile CreateSeededProfile(
        string profileId,
        string name,
        string? executablePath,
        string? windowTitle,
        RoiManagerSettings settings)
    {
        var now = DateTime.UtcNow;
        var regions = GetDefaultVnRegions(settings);
        var heatmap = GetDefaultVnHeatmap(settings);

        return new RoiProfile
        {
            Id = profileId,
            Name = name,
            ExecutablePath = executablePath,
            WindowTitlePattern = windowTitle,
            Regions = regions,
            HeatmapData = heatmap,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
            TotalLearningSessionCount = 0,
            TotalTextDetectionCount = 0,
            IsEnabled = true,
            AutoLearningEnabled = true,
            ExclusionZones = []
        };
    }
}
