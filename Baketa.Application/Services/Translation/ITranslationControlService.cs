using System.Reactive;
using Baketa.Application.Services.Translation;

namespace Baketa.Application.Services.Translation;

/// <summary>
/// 翻訳制御統一サービス
/// MainOverlayViewModelから抽出された翻訳制御・状態管理機能を統一化
/// </summary>
public interface ITranslationControlService
{
    /// <summary>
    /// 翻訳がアクティブかどうか
    /// </summary>
    bool IsTranslationActive { get; }

    /// <summary>
    /// 現在の翻訳状態
    /// </summary>
    TranslationStatus CurrentStatus { get; }

    /// <summary>
    /// 翻訳結果が表示されているかどうか
    /// </summary>
    bool IsTranslationResultVisible { get; }

    /// <summary>
    /// ローディング中かどうか
    /// </summary>
    bool IsLoading { get; }

    /// <summary>
    /// 翻訳の開始/停止を実行します
    /// </summary>
    /// <param name="windowInfo">対象ウィンドウ情報</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>実行結果</returns>
    Task<TranslationControlResult> ExecuteStartStopAsync(
        Core.Abstractions.Platform.Windows.Adapters.WindowInfo? windowInfo,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 翻訳を開始します
    /// </summary>
    /// <param name="windowInfo">対象ウィンドウ情報</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>開始結果</returns>
    Task<TranslationControlResult> StartTranslationAsync(
        Core.Abstractions.Platform.Windows.Adapters.WindowInfo windowInfo,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 翻訳を停止します
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>停止結果</returns>
    Task<TranslationControlResult> StopTranslationAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 翻訳結果の表示/非表示を切り替えます
    /// </summary>
    /// <returns>切り替え結果</returns>
    Task ToggleTranslationVisibilityAsync();

    /// <summary>
    /// 翻訳状態の変更通知
    /// </summary>
    IObservable<TranslationStateChanged> TranslationStateChanged { get; }

    /// <summary>
    /// ローディング状態の変更通知
    /// </summary>
    IObservable<bool> LoadingStateChanged { get; }

    /// <summary>
    /// UI制御状態の派生プロパティ
    /// </summary>
    IObservable<TranslationUIState> UIStateChanged { get; }
}

/// <summary>
/// 翻訳制御実行結果
/// </summary>
public sealed record TranslationControlResult(
    bool IsSuccess,
    string? ErrorMessage = null,
    TranslationStatus? NewStatus = null,
    TimeSpan? ExecutionTime = null
);

/// <summary>
/// 翻訳状態変更イベント
/// </summary>
public sealed record TranslationStateChanged(
    TranslationStatus PreviousStatus,
    TranslationStatus CurrentStatus,
    bool IsTranslationActive,
    bool IsTranslationResultVisible,
    DateTime ChangedAt,
    string Source
);

/// <summary>
/// UI制御状態（派生プロパティ）
/// </summary>
public sealed record TranslationUIState(
    bool ShowHideEnabled,
    bool SettingsEnabled,
    bool IsSelectWindowEnabled,
    string StartStopText,
    string ShowHideText,
    string StatusText
);
