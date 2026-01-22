using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Models.Roi;

namespace Baketa.Core.Abstractions.Roi;

/// <summary>
/// ROIプロファイル管理サービスのインターフェース
/// </summary>
/// <remarks>
/// プロファイルの永続化、検索、管理を担当します。
/// </remarks>
public interface IRoiProfileService
{
    /// <summary>
    /// プロファイルを保存
    /// </summary>
    /// <param name="profile">保存するプロファイル</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    Task SaveProfileAsync(RoiProfile profile, CancellationToken cancellationToken = default);

    /// <summary>
    /// プロファイルをロード
    /// </summary>
    /// <param name="profileId">プロファイルID</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>プロファイル、見つからない場合はnull</returns>
    Task<RoiProfile?> LoadProfileAsync(string profileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// プロファイルが存在するかを確認
    /// </summary>
    /// <param name="profileId">プロファイルID</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>存在する場合true</returns>
    Task<bool> ProfileExistsAsync(string profileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// プロファイルを削除
    /// </summary>
    /// <param name="profileId">プロファイルID</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>削除成功ならtrue</returns>
    Task<bool> DeleteProfileAsync(string profileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 全プロファイルのサマリーを取得
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>プロファイルサマリーのコレクション</returns>
    Task<IReadOnlyList<RoiProfileSummary>> GetAllProfileSummariesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 実行ファイルパスに該当するプロファイルを検索
    /// </summary>
    /// <param name="executablePath">実行ファイルパス</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>該当するプロファイル、見つからない場合はnull</returns>
    Task<RoiProfile?> FindProfileByExecutablePathAsync(
        string executablePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 古いプロファイルをクリーンアップ
    /// </summary>
    /// <param name="maxAge">最大保持期間</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>削除されたプロファイル数</returns>
    Task<int> CleanupOldProfilesAsync(TimeSpan maxAge, CancellationToken cancellationToken = default);

    /// <summary>
    /// プロファイル保存ディレクトリのパスを取得
    /// </summary>
    string ProfilesDirectoryPath { get; }
}

/// <summary>
/// ROIプロファイルのサマリー情報
/// </summary>
public sealed record RoiProfileSummary
{
    /// <summary>
    /// プロファイルID
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// プロファイル名
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// 実行ファイルパス
    /// </summary>
    public string? ExecutablePath { get; init; }

    /// <summary>
    /// ROI領域数
    /// </summary>
    public int RegionCount { get; init; }

    /// <summary>
    /// 総学習セッション数
    /// </summary>
    public int TotalLearningSessionCount { get; init; }

    /// <summary>
    /// プロファイルが有効かどうか
    /// </summary>
    public bool IsEnabled { get; init; }

    /// <summary>
    /// 作成日時
    /// </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// 最終更新日時
    /// </summary>
    public DateTime UpdatedAt { get; init; }

    /// <summary>
    /// ファイルサイズ（バイト）
    /// </summary>
    public long FileSizeBytes { get; init; }

    /// <summary>
    /// プロファイルからサマリーを作成
    /// </summary>
    public static RoiProfileSummary FromProfile(RoiProfile profile, long fileSizeBytes = 0)
    {
        return new RoiProfileSummary
        {
            Id = profile.Id,
            Name = profile.Name,
            ExecutablePath = profile.ExecutablePath,
            RegionCount = profile.Regions.Count,
            TotalLearningSessionCount = profile.TotalLearningSessionCount,
            IsEnabled = profile.IsEnabled,
            CreatedAt = profile.CreatedAt,
            UpdatedAt = profile.UpdatedAt,
            FileSizeBytes = fileSizeBytes
        };
    }
}
