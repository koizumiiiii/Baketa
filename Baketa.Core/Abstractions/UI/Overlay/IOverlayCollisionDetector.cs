using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;

namespace Baketa.Core.Abstractions.UI.Overlay;

/// <summary>
/// オーバーレイ重複・衝突検出インターフェース
/// Phase 13重複防止フィルターを抽象化・拡張したシステム
/// Clean Architecture: Core層 - 抽象化定義
/// </summary>
public interface IOverlayCollisionDetector
{
    /// <summary>
    /// 指定されたオーバーレイ要求が表示可能かを判定
    /// Phase 13重複防止フィルターの統一実装
    /// </summary>
    /// <param name="request">オーバーレイ表示要求</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>表示可能な場合はtrue、重複等で拒否する場合はfalse</returns>
    Task<bool> ShouldDisplayAsync(OverlayDisplayRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 表示されたオーバーレイを登録
    /// 以降の重複検出で参照される
    /// </summary>
    /// <param name="info">表示されたオーバーレイ情報</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    Task RegisterDisplayedAsync(OverlayInfo info, CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定領域内のオーバーレイを検出
    /// 位置衝突検出・領域クリーンアップ用
    /// </summary>
    /// <param name="area">検索対象領域</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>領域内のオーバーレイ情報リスト</returns>
    Task<IEnumerable<OverlayInfo>> DetectCollisionsAsync(Rectangle area, CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定されたオーバーレイの登録を解除
    /// オーバーレイ削除時の後処理
    /// </summary>
    /// <param name="overlayId">解除するオーバーレイID</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    Task UnregisterAsync(string overlayId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 期限切れオーバーレイの自動クリーンアップ
    /// メモリリーク防止・パフォーマンス維持
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>クリーンアップされたオーバーレイ数</returns>
    Task<int> CleanupExpiredAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// すべての登録情報をリセット
    /// アプリケーション停止・リセット時用
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    Task ResetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 現在登録されているオーバーレイ数
    /// デバッグ・監視用
    /// </summary>
    int RegisteredCount { get; }
}

/// <summary>
/// オーバーレイ表示要求データ
/// </summary>
public record OverlayDisplayRequest
{
    /// <summary>
    /// 要求ID（重複検出用）
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
    /// 元テキスト（重複検出の補助情報）
    /// </summary>
    public string? OriginalText { get; init; }

    /// <summary>
    /// 要求タイムスタンプ
    /// </summary>
    public DateTimeOffset RequestTime { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// 翻訳エンジン名（重複検出の補助情報）
    /// </summary>
    public string? EngineName { get; init; }
}

/// <summary>
/// 表示されているオーバーレイ情報
/// </summary>
public record OverlayInfo
{
    /// <summary>
    /// オーバーレイID
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
    /// 表示開始時刻
    /// </summary>
    public DateTimeOffset DisplayStartTime { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// 最終アクセス時刻（TTL管理用）
    /// </summary>
    public DateTimeOffset LastAccessTime { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// 元テキスト
    /// </summary>
    public string? OriginalText { get; init; }

    /// <summary>
    /// 翻訳エンジン名
    /// </summary>
    public string? EngineName { get; init; }

    /// <summary>
    /// オーバーレイの現在の可視状態
    /// </summary>
    public bool IsVisible { get; set; } = true;
}

/// <summary>
/// 重複検出設定
/// Phase 13で実装された設定の抽象化
/// </summary>
public record CollisionDetectionSettings
{
    /// <summary>
    /// 重複防止ウィンドウ期間
    /// 同一テキストの再表示を防ぐ期間
    /// </summary>
    public TimeSpan DuplicationPreventionWindow { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// 自動クリーンアップのしきい値
    /// 登録エントリ数がこの値を超えたら古いものから削除
    /// </summary>
    public int AutoCleanupThreshold { get; init; } = 100;

    /// <summary>
    /// エントリの最大生存時間
    /// この期間を超えたエントリは自動的に削除される
    /// </summary>
    public TimeSpan MaxEntryLifetime { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 位置衝突検出の有効化
    /// 同一位置での重複表示を防ぐかどうか
    /// </summary>
    public bool EnablePositionCollisionDetection { get; init; } = true;

    /// <summary>
    /// 位置衝突判定の許容オーバーラップ率
    /// 0.0 = 完全重複のみ検出, 1.0 = 少しでも重複があれば検出
    /// </summary>
    public double PositionOverlapThreshold { get; init; } = 0.7;
}
