using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Translation;

namespace Baketa.Core.Abstractions.UI.Overlay;

/// <summary>
/// オーバーレイシステム全体の中央調整インターフェース
/// 全ての翻訳結果オーバーレイ要求を統一的に処理し、重複排除を実現
/// Clean Architecture: Core層 - 抽象化定義
/// </summary>
public interface IOverlayOrchestrator
{
    /// <summary>
    /// 翻訳結果をオーバーレイ表示するための統一エントリーポイント
    /// 全ての翻訳結果はこのメソッドを経由して処理される
    /// </summary>
    /// <param name="result">翻訳結果データ</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>オーバーレイ表示が実行された場合はtrue、重複等でスキップされた場合はfalse</returns>
    Task<bool> HandleTranslationResultAsync(TranslationResult result, CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定領域内のオーバーレイを削除
    /// UltraThink Phase対応: 画面変化時の自動クリーンアップ
    /// </summary>
    /// <param name="area">削除対象領域</param>
    /// <param name="excludeId">削除から除外するオーバーレイID</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    Task RemoveOverlaysInAreaAsync(System.Drawing.Rectangle area, string? excludeId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 全オーバーレイの可視性制御
    /// パフォーマンス最適化: 削除・再作成ではなく可視性のみ変更
    /// </summary>
    /// <param name="visible">表示する場合はtrue</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    Task SetAllOverlaysVisibilityAsync(bool visible, CancellationToken cancellationToken = default);

    /// <summary>
    /// すべてのオーバーレイをリセット
    /// アプリケーション終了・停止時の完全クリーンアップ
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    Task ResetAllOverlaysAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 現在アクティブなオーバーレイ数
    /// デバッグ・パフォーマンス監視用
    /// </summary>
    int ActiveOverlayCount { get; }

    /// <summary>
    /// オーケストレーター初期化
    /// 依存サービスの準備・設定読み込み
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 翻訳結果データ（統一形式）
/// 既存のTextChunkから必要データを抽出・標準化
/// </summary>
public record TranslationResult
{
    /// <summary>
    /// 一意識別子（重複検出用）
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// 翻訳結果テキスト
    /// </summary>
    public required string TranslatedText { get; init; }

    /// <summary>
    /// 元テキスト
    /// </summary>
    public required string OriginalText { get; init; }

    /// <summary>
    /// 表示領域
    /// </summary>
    public required System.Drawing.Rectangle DisplayArea { get; init; }

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
    /// タイムスタンプ（重複検出・TTL管理用）
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
