using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;

namespace Baketa.Core.Abstractions.UI.Overlay;

/// <summary>
/// オーバーレイライフサイクル管理インターフェース
/// オーバーレイの作成・更新・削除を統一的に管理
/// Clean Architecture: Core層 - 抽象化定義
/// </summary>
public interface IOverlayLifecycleManager
{
    /// <summary>
    /// 新規オーバーレイを作成
    /// 既存の同じIDがある場合は更新処理に切り替える
    /// </summary>
    /// <param name="request">オーバーレイ作成要求</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>作成されたオーバーレイ情報</returns>
    Task<OverlayInfo> CreateOverlayAsync(OverlayCreationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 既存オーバーレイを更新
    /// テキスト内容・位置・可視性の変更に対応
    /// </summary>
    /// <param name="overlayId">更新対象オーバーレイID</param>
    /// <param name="request">更新要求</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>更新されたオーバーレイ情報、存在しない場合はnull</returns>
    Task<OverlayInfo?> UpdateOverlayAsync(string overlayId, OverlayUpdateRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// オーバーレイを削除
    /// UI要素の削除とリソース解放を実行
    /// </summary>
    /// <param name="overlayId">削除対象オーバーレイID</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>削除が成功した場合はtrue</returns>
    Task<bool> RemoveOverlayAsync(string overlayId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定領域内のオーバーレイを一括削除
    /// 画面変化時の自動クリーンアップ用
    /// </summary>
    /// <param name="area">削除対象領域</param>
    /// <param name="excludeIds">削除から除外するオーバーレイID</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>削除されたオーバーレイ数</returns>
    Task<int> RemoveOverlaysInAreaAsync(Rectangle area, IEnumerable<string>? excludeIds = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 全オーバーレイの可視性を一括制御
    /// パフォーマンス最適化: 削除・再作成ではなく可視性のみ変更
    /// </summary>
    /// <param name="visible">表示する場合はtrue</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>可視性が変更されたオーバーレイ数</returns>
    Task<int> SetAllVisibilityAsync(bool visible, CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定されたオーバーレイの情報を取得
    /// デバッグ・監視用
    /// </summary>
    /// <param name="overlayId">取得対象オーバーレイID</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>オーバーレイ情報、存在しない場合はnull</returns>
    Task<OverlayInfo?> GetOverlayInfoAsync(string overlayId, CancellationToken cancellationToken = default);

    /// <summary>
    /// すべてのアクティブオーバーレイ情報を取得
    /// デバッグ・監視用
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>アクティブなオーバーレイ情報のリスト</returns>
    Task<IEnumerable<OverlayInfo>> GetAllOverlaysAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// すべてのオーバーレイをリセット
    /// 完全クリーンアップ・アプリケーション終了時用
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    Task ResetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 現在管理しているオーバーレイ数
    /// パフォーマンス監視用
    /// </summary>
    int ActiveCount { get; }

    /// <summary>
    /// ライフサイクルマネージャーの初期化
    /// 依存サービスの準備
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// オーバーレイ作成要求データ
/// </summary>
public record OverlayCreationRequest
{
    /// <summary>
    /// オーバーレイID（一意識別子）
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// 表示テキスト
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// 表示領域
    /// </summary>
    public required Rectangle DisplayArea { get; init; }

    /// <summary>
    /// 元テキスト
    /// </summary>
    public string? OriginalText { get; init; }

    /// <summary>
    /// 翻訳元言語
    /// </summary>
    public string? SourceLanguage { get; init; }

    /// <summary>
    /// 翻訳先言語
    /// </summary>
    public string? TargetLanguage { get; init; }

    /// <summary>
    /// 翻訳エンジン名
    /// </summary>
    public string? EngineName { get; init; }

    /// <summary>
    /// 初期可視状態
    /// </summary>
    public bool InitialVisibility { get; init; } = true;

    /// <summary>
    /// Z-index（表示優先度）
    /// </summary>
    public int ZIndex { get; init; } = 0;
}

/// <summary>
/// オーバーレイ更新要求データ
/// </summary>
public record OverlayUpdateRequest
{
    /// <summary>
    /// 更新するテキスト（nullの場合は変更なし）
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// 更新する表示領域（nullの場合は変更なし）
    /// </summary>
    public Rectangle? DisplayArea { get; init; }

    /// <summary>
    /// 更新する可視状態（nullの場合は変更なし）
    /// </summary>
    public bool? Visibility { get; init; }

    /// <summary>
    /// 更新するZ-index（nullの場合は変更なし）
    /// </summary>
    public int? ZIndex { get; init; }

    /// <summary>
    /// 最終アクセス時刻の更新
    /// TTL管理のためのタッチ操作
    /// </summary>
    public bool UpdateLastAccessTime { get; init; } = true;
}

/// <summary>
/// ライフサイクル管理統計情報
/// パフォーマンス監視・デバッグ用
/// </summary>
public record LifecycleStatistics
{
    /// <summary>
    /// 作成されたオーバーレイ総数
    /// </summary>
    public long TotalCreated { get; init; }

    /// <summary>
    /// 更新されたオーバーレイ総数
    /// </summary>
    public long TotalUpdated { get; init; }

    /// <summary>
    /// 削除されたオーバーレイ総数
    /// </summary>
    public long TotalRemoved { get; init; }

    /// <summary>
    /// 現在アクティブなオーバーレイ数
    /// </summary>
    public int CurrentActive { get; init; }

    /// <summary>
    /// ピーク時同時表示オーバーレイ数
    /// </summary>
    public int PeakConcurrent { get; init; }

    /// <summary>
    /// 統計収集開始時刻
    /// </summary>
    public DateTimeOffset StartTime { get; init; }

    /// <summary>
    /// 平均オーバーレイ生存時間
    /// </summary>
    public TimeSpan AverageLifetime { get; init; }
}
