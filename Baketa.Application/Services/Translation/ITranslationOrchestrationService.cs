using System;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Application.Models;
using Baketa.Core.Abstractions.Imaging;

namespace Baketa.Application.Services.Translation;

/// <summary>
/// 翻訳オーケストレーションサービスインターフェース
/// キャプチャ、翻訳、UI表示の統合管理を担当
/// </summary>
public interface ITranslationOrchestrationService
{
    #region 状態プロパティ

    /// <summary>
    /// 自動翻訳が実行中かどうか
    /// </summary>
    bool IsAutomaticTranslationActive { get; }

    /// <summary>
    /// 単発翻訳が実行中かどうか
    /// </summary>
    bool IsSingleTranslationActive { get; }

    /// <summary>
    /// 何らかの翻訳処理が実行中かどうか
    /// </summary>
    bool IsAnyTranslationActive { get; }

    /// <summary>
    /// 現在の翻訳モード
    /// </summary>
    TranslationMode CurrentMode { get; }

    #endregion

    #region 翻訳実行メソッド

    /// <summary>
    /// 自動翻訳を開始します
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>自動翻訳実行タスク</returns>
    Task StartAutomaticTranslationAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 自動翻訳を停止します
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>停止タスク</returns>
    Task StopAutomaticTranslationAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 単発翻訳を実行します
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>単発翻訳実行タスク</returns>
    Task TriggerSingleTranslationAsync(CancellationToken cancellationToken = default);

    #endregion

    #region Observable ストリーム

    /// <summary>
    /// 翻訳結果のストリーム
    /// </summary>
    IObservable<TranslationResult> TranslationResults { get; }

    /// <summary>
    /// 翻訳状態変更のストリーム
    /// </summary>
    IObservable<TranslationStatus> StatusChanges { get; }

    /// <summary>
    /// 翻訳進行状況のストリーム
    /// </summary>
    IObservable<TranslationProgress> ProgressUpdates { get; }

    #endregion

    #region 設定管理

    /// <summary>
    /// 単発翻訳の表示時間（秒）を取得します
    /// </summary>
    /// <returns>表示時間</returns>
    TimeSpan GetSingleTranslationDisplayDuration();

    /// <summary>
    /// 自動翻訳の間隔を取得します
    /// </summary>
    /// <returns>翻訳間隔</returns>
    TimeSpan GetAutomaticTranslationInterval();

    /// <summary>
    /// 翻訳設定を更新します
    /// </summary>
    /// <param name="settings">翻訳設定</param>
    /// <returns>更新タスク</returns>
    Task UpdateTranslationSettingsAsync(TranslationSettings settings);

    #endregion

    #region リソース管理

    /// <summary>
    /// サービスを開始します
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>開始タスク</returns>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// サービスを停止します
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>停止タスク</returns>
    Task StopAsync(CancellationToken cancellationToken = default);

    #endregion
}

/// <summary>
/// 翻訳結果
/// </summary>
public sealed record TranslationResult
{
    /// <summary>
    /// 翻訳ID
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// 翻訳モード
    /// </summary>
    public required TranslationMode Mode { get; init; }

    /// <summary>
    /// 元のテキスト
    /// </summary>
    public required string OriginalText { get; init; }

    /// <summary>
    /// 翻訳されたテキスト
    /// </summary>
    public required string TranslatedText { get; init; }

    /// <summary>
    /// 検出された言語
    /// </summary>
    public string? DetectedLanguage { get; init; }

    /// <summary>
    /// 翻訳先言語
    /// </summary>
    public required string TargetLanguage { get; init; }

    /// <summary>
    /// 翻訳信頼度 (0.0-1.0)
    /// </summary>
    public float Confidence { get; init; }

    /// <summary>
    /// キャプチャされた画像
    /// </summary>
    public IImage? CapturedImage { get; init; }

    /// <summary>
    /// 翻訳完了時刻
    /// </summary>
    public DateTime CompletedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 処理時間
    /// </summary>
    public TimeSpan ProcessingTime { get; init; }

    /// <summary>
    /// 表示時間（単発翻訳の場合）
    /// </summary>
    public TimeSpan? DisplayDuration { get; init; }
}

/// <summary>
/// 翻訳状態
/// </summary>
public enum TranslationStatus
{
    /// <summary>
    /// アイドル状態
    /// </summary>
    Idle,

    /// <summary>
    /// キャプチャ中
    /// </summary>
    Capturing,

    /// <summary>
    /// OCR処理中
    /// </summary>
    ProcessingOCR,

    /// <summary>
    /// 翻訳中
    /// </summary>
    Translating,

    /// <summary>
    /// 完了
    /// </summary>
    Completed,

    /// <summary>
    /// エラー
    /// </summary>
    Error,

    /// <summary>
    /// キャンセル済み
    /// </summary>
    Cancelled
}

/// <summary>
/// 翻訳進行状況
/// </summary>
public sealed record TranslationProgress
{
    /// <summary>
    /// 翻訳ID
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// 現在の状態
    /// </summary>
    public required TranslationStatus Status { get; init; }

    /// <summary>
    /// 進行率 (0.0-1.0)
    /// </summary>
    public float Progress { get; init; }

    /// <summary>
    /// ステータスメッセージ
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// 更新時刻
    /// </summary>
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// 翻訳設定
/// </summary>
public sealed record TranslationSettings
{
    /// <summary>
    /// 単発翻訳の表示時間（秒）
    /// </summary>
    public int SingleTranslationDisplaySeconds { get; init; } = 5;

    /// <summary>
    /// 自動翻訳の間隔（ミリ秒）
    /// </summary>
    public int AutomaticTranslationIntervalMs { get; init; } = 1000;

    /// <summary>
    /// 翻訳結果の最大保持数
    /// </summary>
    public int MaxResultHistory { get; init; } = 10;

    /// <summary>
    /// OCRの信頼度閾値
    /// </summary>
    public float OcrConfidenceThreshold { get; init; } = 0.7f;

    /// <summary>
    /// 翻訳の信頼度閾値
    /// </summary>
    public float TranslationConfidenceThreshold { get; init; } = 0.5f;

    /// <summary>
    /// 差分検出の閾値
    /// </summary>
    public float ChangeDetectionThreshold { get; init; } = 0.05f;
}
