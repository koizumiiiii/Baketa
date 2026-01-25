using System;
using System.Collections.Generic;

namespace Baketa.Core.Models.Roi;

/// <summary>
/// ROIプロファイルを表すモデル
/// </summary>
/// <remarks>
/// ゲーム/ウィンドウごとのROI学習データを保持します。
/// exePathのハッシュによってプロファイルを識別します。
/// </remarks>
public sealed record RoiProfile
{
    /// <summary>
    /// プロファイルの一意識別子（exePathのSHA256ハッシュ）
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// プロファイル名（表示用）
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// 対象の実行ファイルパス
    /// </summary>
    public string? ExecutablePath { get; init; }

    /// <summary>
    /// 対象のウィンドウタイトルパターン
    /// </summary>
    public string? WindowTitlePattern { get; init; }

    /// <summary>
    /// 学習されたROI領域のコレクション
    /// </summary>
    public IReadOnlyList<RoiRegion> Regions { get; init; } = [];

    /// <summary>
    /// ヒートマップデータ
    /// </summary>
    public RoiHeatmapData? HeatmapData { get; init; }

    /// <summary>
    /// プロファイルのバージョン（マイグレーション用）
    /// </summary>
    public int Version { get; init; } = 1;

    /// <summary>
    /// プロファイルが作成された時刻
    /// </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// プロファイルが最後に更新された時刻
    /// </summary>
    public DateTime UpdatedAt { get; init; }

    /// <summary>
    /// 総学習セッション数
    /// </summary>
    public int TotalLearningSessionCount { get; init; }

    /// <summary>
    /// 総テキスト検出回数
    /// </summary>
    public long TotalTextDetectionCount { get; init; }

    /// <summary>
    /// プロファイルが有効かどうか
    /// </summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>
    /// 自動学習が有効かどうか
    /// </summary>
    public bool AutoLearningEnabled { get; init; } = true;

    /// <summary>
    /// 除外ゾーンのコレクション
    /// </summary>
    /// <remarks>
    /// 翻訳不要な領域（ボタン、アイコンなど）を定義します。
    /// </remarks>
    public IReadOnlyList<NormalizedRect> ExclusionZones { get; init; } = [];

    /// <summary>
    /// 設定値の妥当性を検証
    /// </summary>
    public bool IsValid()
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Name))
        {
            return false;
        }

        if (Version < 1)
        {
            return false;
        }

        // 全てのROI領域が有効であることを確認
        foreach (var region in Regions)
        {
            if (!region.IsValid())
            {
                return false;
            }
        }

        // 全ての除外ゾーンが有効であることを確認
        foreach (var zone in ExclusionZones)
        {
            if (!zone.IsValid())
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 指定した正規化座標に該当するROI領域を検索
    /// </summary>
    /// <param name="normalizedX">正規化X座標（0.0-1.0）</param>
    /// <param name="normalizedY">正規化Y座標（0.0-1.0）</param>
    /// <returns>該当するROI領域、なければnull</returns>
    public RoiRegion? FindRegionAt(float normalizedX, float normalizedY)
    {
        foreach (var region in Regions)
        {
            var bounds = region.NormalizedBounds;
            if (normalizedX >= bounds.X && normalizedX <= bounds.Right &&
                normalizedY >= bounds.Y && normalizedY <= bounds.Bottom)
            {
                return region;
            }
        }

        return null;
    }

    /// <summary>
    /// 指定した正規化矩形と重複するROI領域を検索
    /// </summary>
    /// <param name="bounds">検索対象の正規化矩形</param>
    /// <param name="minIoU">最小IoU閾値</param>
    /// <returns>重複するROI領域のコレクション</returns>
    public IEnumerable<RoiRegion> FindOverlappingRegions(NormalizedRect bounds, float minIoU = 0.1f)
    {
        foreach (var region in Regions)
        {
            var iou = region.NormalizedBounds.CalculateIoU(bounds);
            if (iou >= minIoU)
            {
                yield return region;
            }
        }
    }

    /// <summary>
    /// 指定した座標が除外ゾーン内かどうかを判定
    /// </summary>
    /// <param name="normalizedX">正規化X座標（0.0-1.0）</param>
    /// <param name="normalizedY">正規化Y座標（0.0-1.0）</param>
    /// <returns>除外ゾーン内ならtrue</returns>
    public bool IsInExclusionZone(float normalizedX, float normalizedY)
    {
        foreach (var zone in ExclusionZones)
        {
            if (normalizedX >= zone.X && normalizedX <= zone.Right &&
                normalizedY >= zone.Y && normalizedY <= zone.Bottom)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 領域を追加した新しいプロファイルを作成
    /// </summary>
    public RoiProfile WithRegion(RoiRegion region)
    {
        var newRegions = new List<RoiRegion>(Regions) { region };
        return this with
        {
            Regions = newRegions,
            UpdatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// 領域を更新した新しいプロファイルを作成
    /// </summary>
    public RoiProfile WithUpdatedRegion(RoiRegion updatedRegion)
    {
        var newRegions = new List<RoiRegion>();
        var found = false;

        foreach (var region in Regions)
        {
            if (region.Id == updatedRegion.Id)
            {
                newRegions.Add(updatedRegion);
                found = true;
            }
            else
            {
                newRegions.Add(region);
            }
        }

        if (!found)
        {
            newRegions.Add(updatedRegion);
        }

        return this with
        {
            Regions = newRegions,
            UpdatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// 学習セッション数をインクリメントした新しいプロファイルを作成
    /// </summary>
    public RoiProfile WithLearningSession()
    {
        return this with
        {
            TotalLearningSessionCount = TotalLearningSessionCount + 1,
            UpdatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// 新しいプロファイルを作成
    /// </summary>
    /// <param name="id">プロファイルID</param>
    /// <param name="name">プロファイル名</param>
    /// <param name="executablePath">実行ファイルパス（オプション）</param>
    /// <param name="windowTitlePattern">ウィンドウタイトルパターン（オプション）</param>
    public static RoiProfile Create(string id, string name, string? executablePath = null, string? windowTitlePattern = null)
    {
        var now = DateTime.UtcNow;
        return new RoiProfile
        {
            Id = id,
            Name = name,
            ExecutablePath = executablePath,
            WindowTitlePattern = windowTitlePattern,
            CreatedAt = now,
            UpdatedAt = now
        };
    }
}
