namespace Baketa.Core.Abstractions.Services;

/// <summary>
/// [Issue #256] コンポーネント更新チェック結果インターフェース
/// Clean Architecture: UI層がInfrastructure層の具象型に直接依存しないための抽象化
/// </summary>
public interface IComponentUpdateCheckResult
{
    /// <summary>
    /// コンポーネントID
    /// </summary>
    string ComponentId { get; }

    /// <summary>
    /// 表示名
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// 現在のバージョン（未インストールの場合はnull）
    /// </summary>
    string? CurrentVersion { get; }

    /// <summary>
    /// 最新バージョン
    /// </summary>
    string LatestVersion { get; }

    /// <summary>
    /// 更新が利用可能か
    /// </summary>
    bool UpdateAvailable { get; }

    /// <summary>
    /// 変更履歴
    /// </summary>
    string? Changelog { get; }

    /// <summary>
    /// ダウンロードサイズ合計（バイト）
    /// </summary>
    long TotalDownloadSize { get; }

    /// <summary>
    /// アプリバージョンが要件を満たしているか
    /// </summary>
    bool MeetsAppVersionRequirement { get; }
}
