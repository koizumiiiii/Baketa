using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Events;
using Baketa.UI.Configuration;
using Baketa.UI.Framework;
using Baketa.UI.Models;
using Baketa.UI.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReactiveUI;
using EngineStatus = Baketa.UI.Services.TranslationEngineStatus;
using StatusUpdate = Baketa.UI.Services.TranslationEngineStatusUpdate;

namespace Baketa.UI.ViewModels.Settings;

/// <summary>
/// 翻訳エンジン選択ViewModel
/// </summary>
public sealed class EngineSelectionViewModel : Framework.ViewModelBase, IActivatableViewModel
{
    private readonly ITranslationEngineStatusService _statusService;
    private readonly IUserPlanService _planService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<EngineSelectionViewModel> _logger;
    private readonly TranslationUIOptions _options;
    private readonly CompositeDisposable _disposables = [];

    private TranslationEngine _selectedEngine = TranslationEngine.LocalOnly;
    private string _selectedEngineDescription = string.Empty;
    private bool _isCloudOnlyEnabled;
    private bool _hasStatusWarning;
    private string _statusWarningMessage = string.Empty;
    private bool _isLoading;

    /// <summary>
    /// ViewModel活性化管理
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CS0109:Member does not hide an accessible member", Justification = "Intentional shadowing for independent activation management")]
    public new ViewModelActivator Activator { get; } = new();

    /// <summary>
    /// 現在選択されている翻訳エンジン
    /// </summary>
    public TranslationEngine SelectedEngine
    {
        get => _selectedEngine;
        set => this.RaiseAndSetIfChanged(ref _selectedEngine, value);
    }

    /// <summary>
    /// 選択されたエンジンの説明文
    /// </summary>
    public string SelectedEngineDescription
    {
        get => _selectedEngineDescription;
        private set => this.RaiseAndSetIfChanged(ref _selectedEngineDescription, value);
    }

    /// <summary>
    /// CloudOnlyエンジンが利用可能かどうか
    /// </summary>
    public bool IsCloudOnlyEnabled
    {
        get => _isCloudOnlyEnabled;
        private set => this.RaiseAndSetIfChanged(ref _isCloudOnlyEnabled, value);
    }

    /// <summary>
    /// 状態警告があるかどうか
    /// </summary>
    public bool HasStatusWarning
    {
        get => _hasStatusWarning;
        private set => this.RaiseAndSetIfChanged(ref _hasStatusWarning, value);
    }

    /// <summary>
    /// 状態警告メッセージ
    /// </summary>
    public string StatusWarningMessage
    {
        get => _statusWarningMessage;
        private set => this.RaiseAndSetIfChanged(ref _statusWarningMessage, value);
    }

    /// <summary>
    /// ローディング中かどうか
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CS0109:Member does not hide an accessible member", Justification = "Intentional shadowing for independent loading state management")]
    public new bool IsLoading
    {
        get => _isLoading;
        private set => this.RaiseAndSetIfChanged(ref _isLoading, value);
    }

    /// <summary>
    /// 利用可能なエンジン一覧
    /// </summary>
    public IEnumerable<TranslationEngineItem> AvailableEngines { get; }

    /// <summary>
    /// LocalOnlyエンジンの状態
    /// </summary>
    public EngineStatus LocalEngineStatus => _statusService.LocalEngineStatus;

    /// <summary>
    /// CloudOnlyエンジンの状態
    /// </summary>
    public EngineStatus CloudEngineStatus => _statusService.CloudEngineStatus;

    /// <summary>
    /// エンジン選択変更コマンド
    /// </summary>
    public ReactiveCommand<TranslationEngine, Unit> SelectEngineCommand { get; }

    /// <summary>
    /// 状態更新コマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> RefreshStatusCommand { get; }

    /// <summary>
    /// プレミアムプラン案内表示コマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> ShowPremiumInfoCommand { get; }

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public EngineSelectionViewModel(
        ITranslationEngineStatusService statusService,
        IUserPlanService planService,
        INotificationService notificationService,
        IOptions<TranslationUIOptions> options,
        ILogger<EngineSelectionViewModel> logger,
        IEventAggregator eventAggregator) : base(eventAggregator)
    {
        ArgumentNullException.ThrowIfNull(statusService);
        ArgumentNullException.ThrowIfNull(planService);
        ArgumentNullException.ThrowIfNull(notificationService);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);

        _statusService = statusService;
        _planService = planService;
        _notificationService = notificationService;
        _logger = logger;
        _options = options.Value;

        // 利用可能エンジンリストの作成
        var engines = CreateAvailableEnginesList();
        AvailableEngines = [.. engines];

        // 初期設定
        SelectedEngine = ParseEngineFromString(_options.DefaultEngineStrategy);
        UpdateEngineDescription();
        UpdateCloudOnlyAvailability();

        // コマンドの作成
        var canSelectEngine = this.WhenAnyValue(x => x.IsLoading).Select(loading => !loading);
        SelectEngineCommand = ReactiveCommand.CreateFromTask<TranslationEngine>(SelectEngineAsync, canSelectEngine);

        RefreshStatusCommand = ReactiveCommand.CreateFromTask(RefreshStatusAsync, canSelectEngine);

        var canShowPremiumInfo = this.WhenAnyValue(x => x.IsCloudOnlyEnabled).Select(enabled => !enabled);
        ShowPremiumInfoCommand = ReactiveCommand.Create(ShowPremiumInfo, canShowPremiumInfo);

        // ViewModel活性化時の処理
        this.WhenActivated(disposables =>
        {
            // プラン変更の監視
            Observable.FromEventPattern<UserPlanChangedEventArgs>(
                h => _planService.PlanChanged += h,
                h => _planService.PlanChanged -= h)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ => UpdateCloudOnlyAvailability())
                .DisposeWith(disposables);

            // エンジン状態更新の監視
            _statusService.StatusUpdates
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(update => OnStatusUpdate(update))
                .DisposeWith(disposables);

            // 選択エンジン変更時の処理
            this.WhenAnyValue(x => x.SelectedEngine)
                .Skip(1) // 初期値をスキップ
                .Subscribe(_ => UpdateEngineDescription())
                .DisposeWith(disposables);

            // 状態チェック開始
            RefreshStatusCommand.Execute().Subscribe()
                .DisposeWith(disposables);

            _logger.LogDebug("EngineSelectionViewModel activated");
        });

        _logger.LogInformation("EngineSelectionViewModel created with default engine: {Engine}", SelectedEngine);
    }

    /// <summary>
    /// エンジン選択処理
    /// </summary>
    private async Task SelectEngineAsync(TranslationEngine engine)
    {
        if (SelectedEngine == engine)
            return;

        _logger.LogInformation("Changing engine from {OldEngine} to {NewEngine}", SelectedEngine, engine);

        try
        {
            IsLoading = true;

            // CloudOnlyエンジンが選択されたが利用不可の場合
            if (engine == TranslationEngine.CloudOnly && !IsCloudOnlyEnabled)
            {
                await _notificationService.ShowWarningAsync(
                "CloudOnlyエンジンの利用",
                "CloudOnlyエンジンはプレミアムプランでのみ利用可能です。").ConfigureAwait(false);
                return;
            }

            // エンジン状態の確認
            if (engine == TranslationEngine.CloudOnly && !CloudEngineStatus.IsOnline)
            {
                await _notificationService.ShowWarningAsync(
                "CloudOnlyエンジンの状態",
                "CloudOnlyエンジンは現在利用できません。ネットワーク接続を確認してください。").ConfigureAwait(false);

                // それでも選択する場合は警告を表示して継続
                HasStatusWarning = true;
                StatusWarningMessage = "CloudOnlyエンジンはオフラインです";
            }
            else
            {
                HasStatusWarning = false;
                StatusWarningMessage = string.Empty;
            }

            SelectedEngine = engine;

            // 成功通知
            if (_options.EnableNotifications)
            {
                await _notificationService.ShowSuccessAsync(
                    "エンジン選択",
                    $"{GetEngineDisplayName(engine)}に切り替えました。").ConfigureAwait(false);
            }

            _logger.LogInformation("Engine successfully changed to {Engine}", engine);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Failed to select engine {Engine} due to invalid operation", engine);
            await _notificationService.ShowErrorAsync(
                "エンジン選択エラー",
                $"エンジンの切り替えに失敗しました: {ex.Message}").ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Unauthorized access when selecting engine {Engine}", engine);
            await _notificationService.ShowErrorAsync(
                "アクセスエラー",
                "エンジンへのアクセスが拒否されました。プランを確認してください。").ConfigureAwait(false);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Engine selection was cancelled for engine {Engine}", engine);
            await _notificationService.ShowWarningAsync(
                "エンジン選択キャンセル",
                "エンジンの選択がキャンセルされました。").ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            _logger.LogError(ex, "Engine selection timed out for engine {Engine}", engine);
            await _notificationService.ShowErrorAsync(
                "タイムアウトエラー",
                "エンジン選択がタイムアウトしました。再試行してください。").ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not (TaskCanceledException or TimeoutException or InvalidOperationException or UnauthorizedAccessException))
        {
            _logger.LogError(ex, "Unexpected error when selecting engine {Engine}", engine);
            await _notificationService.ShowErrorAsync(
                "エンジン選択エラー",
                $"予期しないエラーが発生しました: {ex.Message}").ConfigureAwait(false);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// 状態更新処理
    /// </summary>
    private async Task RefreshStatusAsync()
    {
        try
        {
            IsLoading = true;
            await _statusService.RefreshStatusAsync().ConfigureAwait(false);
            UpdateCloudOnlyAvailability();

            _logger.LogDebug("Engine status refreshed successfully");
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Engine status refresh was cancelled");
            await _notificationService.ShowWarningAsync(
                "状態更新キャンセル",
                "エンジン状態の更新がキャンセルされました。").ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            _logger.LogError(ex, "Engine status refresh timed out");
            await _notificationService.ShowErrorAsync(
                "タイムアウトエラー",
                "エンジン状態の更新がタイムアウトしました。").ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation during engine status refresh");
            await _notificationService.ShowErrorAsync(
                "操作エラー",
                "現在の状態では更新できません。しばらく待ってから再試行してください。").ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not (TaskCanceledException or TimeoutException or InvalidOperationException))
        {
            _logger.LogError(ex, "Unexpected error during engine status refresh");
            await _notificationService.ShowErrorAsync(
                "状態更新エラー",
                $"予期しないエラーが発生しました: {ex.Message}").ConfigureAwait(false);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// プレミアム情報表示
    /// </summary>
    private void ShowPremiumInfo()
    {
        _notificationService.ShowInfoAsync(
            "プレミアムプラン",
            "CloudOnlyエンジンを利用するにはプレミアムプランへのアップグレードが必要です。\n" +
            "プレミアムプランでは無制限の高品質翻訳をご利用いただけます。");
    }

    /// <summary>
    /// 状態更新イベントの処理
    /// </summary>
    private void OnStatusUpdate(StatusUpdate update)
    {
        _logger.LogDebug("Status update received: {Engine} - {Type}", update.EngineName, update.UpdateType);

        // 選択中エンジンの状態変化を監視
        if ((update.EngineName == "CloudOnly" && SelectedEngine == TranslationEngine.CloudOnly) ||
            (update.EngineName == "LocalOnly" && SelectedEngine == TranslationEngine.LocalOnly))
        {
            UpdateEngineDescription();

            // エラー状態の警告表示
            if (update.UpdateType == StatusUpdateType.ErrorOccurred)
            {
                HasStatusWarning = true;
                StatusWarningMessage = $"{update.EngineName}エンジンでエラーが発生しました";
            }
            else if (update.UpdateType == StatusUpdateType.Recovery)
            {
                HasStatusWarning = false;
                StatusWarningMessage = string.Empty;
            }
        }

        // フォールバック発生通知
        if (update.UpdateType == StatusUpdateType.FallbackTriggered && _options.ShowFallbackInformation)
        {
            var fallback = _statusService.LastFallback;
            if (fallback != null)
            {
                _notificationService.ShowInfoAsync(
                    "フォールバック発生",
                    $"{fallback.FromEngine} → {fallback.ToEngine}\n理由: {fallback.Reason}");
            }
        }
    }

    /// <summary>
    /// CloudOnlyエンジンの利用可否を更新
    /// </summary>
    private void UpdateCloudOnlyAvailability()
    {
        IsCloudOnlyEnabled = _planService.CanUseCloudOnlyEngine;

        if (!IsCloudOnlyEnabled && SelectedEngine == TranslationEngine.CloudOnly)
        {
            // プランダウングレード時のフォールバック
            SelectedEngine = TranslationEngine.LocalOnly;
            _logger.LogInformation("Fallback to LocalOnly due to plan limitation");
        }
    }

    /// <summary>
    /// エンジン説明文の更新
    /// </summary>
    private void UpdateEngineDescription()
    {
        SelectedEngineDescription = SelectedEngine switch
        {
            TranslationEngine.LocalOnly => GetLocalOnlyDescription(),
            TranslationEngine.CloudOnly => GetCloudOnlyDescription(),
            _ => "不明なエンジン"
        };
    }

    /// <summary>
    /// LocalOnlyエンジンの説明取得
    /// </summary>
    private string GetLocalOnlyDescription()
    {
        var status = LocalEngineStatus;
        var baseDesc = "NLLB-200モデルを使用したローカル翻訳。高品質・無料・オフライン対応。";

        if (!status.IsHealthy)
        {
            return $"{baseDesc}\n⚠️ 状態: エラー ({status.LastError})";
        }

        return $"{baseDesc}\n✅ 状態: 正常";
    }

    /// <summary>
    /// CloudOnlyエンジンの説明取得
    /// </summary>
    private string GetCloudOnlyDescription()
    {
        if (!IsCloudOnlyEnabled)
        {
            return "Gemini APIを使用した高品質クラウド翻訳。\n❌ プレミアムプランが必要です。";
        }

        var status = CloudEngineStatus;
        var baseDesc = "Gemini APIを使用した高品質クラウド翻訳。";

        if (!status.IsOnline)
        {
            return $"{baseDesc}\n⚠️ 状態: オフライン";
        }

        if (!status.IsHealthy)
        {
            return $"{baseDesc}\n⚠️ 状態: エラー ({status.LastError})";
        }

        if (status.RemainingRequests <= 10)
        {
            return $"{baseDesc}\n⚠️ 残り回数: {status.RemainingRequests}回";
        }

        return $"{baseDesc}\n✅ 状態: 正常";
    }

    /// <summary>
    /// 利用可能エンジンリストの作成
    /// </summary>
    private static IReadOnlyList<TranslationEngineItem> CreateAvailableEnginesList()
    {
        return
        [
            new(
                TranslationEngine.LocalOnly,
                "LocalOnly",
                "ローカル翻訳",
                "NLLB-200モデル使用、高品質・無料・オフライン対応"),
            new(
                TranslationEngine.CloudOnly,
                "CloudOnly",
                "クラウド翻訳",
                "Gemini API使用、高品質・プレミアムプラン必須")
        ];
    }

    /// <summary>
    /// エンジン表示名の取得
    /// </summary>
    private static string GetEngineDisplayName(TranslationEngine engine)
    {
        return engine switch
        {
            TranslationEngine.LocalOnly => "LocalOnlyエンジン",
            TranslationEngine.CloudOnly => "CloudOnlyエンジン",
            _ => "不明なエンジン"
        };
    }

    /// <summary>
    /// 文字列からエンジンタイプをパース
    /// </summary>
    private static TranslationEngine ParseEngineFromString(string engineString)
    {
        return engineString?.ToUpperInvariant() switch
        {
            "LOCALONLY" => TranslationEngine.LocalOnly,
            "CLOUDONLY" => TranslationEngine.CloudOnly,
            _ => TranslationEngine.LocalOnly // デフォルト
        };
    }

    /// <summary>
    /// リソースを解放します
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _disposables?.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// 翻訳エンジン選択項目
/// </summary>
public sealed record TranslationEngineItem(
    TranslationEngine Engine,
    string Id,
    string DisplayName,
    string Description);
