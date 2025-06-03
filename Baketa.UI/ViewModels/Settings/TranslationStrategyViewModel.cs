using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReactiveUI;
using Baketa.UI.Configuration;
using Baketa.UI.Models;
using Baketa.UI.Services;

namespace Baketa.UI.ViewModels.Settings;

/// <summary>
/// 翻訳戦略選択ViewModel
/// </summary>
public sealed class TranslationStrategyViewModel : ViewModelBase, IActivatableViewModel, IDisposable
{
    private readonly ITranslationEngineStatusService _statusService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<TranslationStrategyViewModel> _logger;
    private readonly TranslationUIOptions _options;
    private readonly CompositeDisposable _disposables = [];

    private TranslationStrategy _selectedStrategy = TranslationStrategy.Direct;
    private string _selectedStrategyDescription = string.Empty;
    private bool _isTwoStageAvailable = true;
    private bool _isLoading;
    private bool _enableFallback = true;
    private string _currentLanguagePair = "ja-en";
    private bool _hasStrategyWarning;
    private string _strategyWarningMessage = string.Empty;

    /// <summary>
    /// ViewModel活性化管理
    /// </summary>
    public ViewModelActivator Activator { get; } = new();

    /// <summary>
    /// 現在選択されている翻訳戦略
    /// </summary>
    public TranslationStrategy SelectedStrategy
    {
        get => _selectedStrategy;
        set => this.RaiseAndSetIfChanged(ref _selectedStrategy, value);
    }

    /// <summary>
    /// 選択された戦略の説明文
    /// </summary>
    public string SelectedStrategyDescription
    {
        get => _selectedStrategyDescription;
        private set => this.RaiseAndSetIfChanged(ref _selectedStrategyDescription, value);
    }

    /// <summary>
    /// 2段階翻訳が利用可能かどうか
    /// </summary>
    public bool IsTwoStageAvailable
    {
        get => _isTwoStageAvailable;
        private set => this.RaiseAndSetIfChanged(ref _isTwoStageAvailable, value);
    }

    /// <summary>
    /// ローディング中かどうか
    /// </summary>
    public bool IsLoading
    {
        get => _isLoading;
        private set => this.RaiseAndSetIfChanged(ref _isLoading, value);
    }

    /// <summary>
    /// フォールバック機能が有効かどうか
    /// </summary>
    public bool EnableFallback
    {
        get => _enableFallback;
        set => this.RaiseAndSetIfChanged(ref _enableFallback, value);
    }

    /// <summary>
    /// 現在の言語ペア
    /// </summary>
    public string CurrentLanguagePair
    {
        get => _currentLanguagePair;
        set => this.RaiseAndSetIfChanged(ref _currentLanguagePair, value);
    }

    /// <summary>
    /// 戦略警告があるかどうか
    /// </summary>
    public bool HasStrategyWarning
    {
        get => _hasStrategyWarning;
        private set => this.RaiseAndSetIfChanged(ref _hasStrategyWarning, value);
    }

    /// <summary>
    /// 戦略警告メッセージ
    /// </summary>
    public string StrategyWarningMessage
    {
        get => _strategyWarningMessage;
        private set => this.RaiseAndSetIfChanged(ref _strategyWarningMessage, value);
    }

    /// <summary>
    /// 利用可能な翻訳戦略一覧
    /// </summary>
    public IEnumerable<TranslationStrategyItem> AvailableStrategies { get; }

    /// <summary>
    /// LocalOnlyエンジンの状態
    /// </summary>
    public TranslationEngineStatus LocalEngineStatus => _statusService.LocalEngineStatus;

    /// <summary>
    /// CloudOnlyエンジンの状態
    /// </summary>
    public TranslationEngineStatus CloudEngineStatus => _statusService.CloudEngineStatus;

    /// <summary>
    /// 戦略選択コマンド
    /// </summary>
    public ReactiveCommand<TranslationStrategy, Unit> SelectStrategyCommand { get; }

    /// <summary>
    /// フォールバック設定切り替えコマンド
    /// </summary>
    public ReactiveCommand<bool, Unit> ToggleFallbackCommand { get; }

    /// <summary>
    /// 戦略詳細表示コマンド
    /// </summary>
    public ReactiveCommand<TranslationStrategy, Unit> ShowStrategyDetailsCommand { get; }

    /// <summary>
    /// 設定リセットコマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> ResetToDefaultCommand { get; }

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public TranslationStrategyViewModel(
        ITranslationEngineStatusService statusService,
        INotificationService notificationService,
        IOptions<TranslationUIOptions> options,
        ILogger<TranslationStrategyViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(statusService);
        ArgumentNullException.ThrowIfNull(notificationService);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);
        
        _statusService = statusService;
        _notificationService = notificationService;
        _logger = logger;
        _options = options.Value;

        // 利用可能戦略リストの作成
        AvailableStrategies = [.. CreateAvailableStrategiesList()];

        // 初期設定
        SelectedStrategy = ParseStrategyFromString(_options.DefaultTranslationStrategy ?? "direct");
        CurrentLanguagePair = _options.DefaultLanguagePair ?? "ja-en";
        UpdateStrategyDescription();
        UpdateTwoStageAvailability();

        // コマンドの作成
        var canExecute = this.WhenAnyValue(x => x.IsLoading).Select(loading => !loading);
        
        SelectStrategyCommand = ReactiveCommand.CreateFromTask<TranslationStrategy>(
            SelectStrategyAsync, canExecute);

        ToggleFallbackCommand = ReactiveCommand.CreateFromTask<bool>(
            ToggleFallbackAsync, canExecute);

        ShowStrategyDetailsCommand = ReactiveCommand.Create<TranslationStrategy>(ShowStrategyDetails);

        ResetToDefaultCommand = ReactiveCommand.CreateFromTask(ResetToDefaultAsync, canExecute);

        // ViewModel活性化時の処理
        this.WhenActivated(disposables =>
        {
            // 戦略選択変更時の処理
            this.WhenAnyValue(x => x.SelectedStrategy)
                .Skip(1) // 初期値をスキップ
                .Subscribe(_ => UpdateStrategyDescription())
                .DisposeWith(disposables);

            // 言語ペア変更時の処理
            this.WhenAnyValue(x => x.CurrentLanguagePair)
                .Subscribe(OnLanguagePairChanged)
                .DisposeWith(disposables);

            // エンジン状態更新の監視
            _statusService.StatusUpdates
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(OnEngineStatusUpdate)
                .DisposeWith(disposables);

            _logger.LogDebug("TranslationStrategyViewModel activated");
        });

        _logger.LogInformation("TranslationStrategyViewModel created with default strategy: {Strategy}", SelectedStrategy);
    }

    /// <summary>
    /// 戦略選択処理
    /// </summary>
    private async Task SelectStrategyAsync(TranslationStrategy strategy)
    {
        if (SelectedStrategy == strategy)
            return;

        _logger.LogInformation("Changing strategy from {OldStrategy} to {NewStrategy}", SelectedStrategy, strategy);

        try
        {
            IsLoading = true;

            // 2段階翻訳が選択されたが利用不可の場合
            if (strategy == TranslationStrategy.TwoStage && !IsTwoStageAvailable)
            {
                await _notificationService.ShowWarningAsync(
                "翻訳戦略選択",
                "2段階翻訳は現在の言語ペアでは利用できません。").ConfigureAwait(false);
                return;
            }

            // エンジン状態の確認
            var warning = await ValidateStrategyAsync(strategy).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(warning))
            {
                HasStrategyWarning = true;
                StrategyWarningMessage = warning;
                
                await _notificationService.ShowWarningAsync(
                    "翻訳戦略の注意事項",
                    warning).ConfigureAwait(false);
            }
            else
            {
                HasStrategyWarning = false;
                StrategyWarningMessage = string.Empty;
            }

            SelectedStrategy = strategy;

            // 成功通知
            if (_options.EnableNotifications)
            {
                await _notificationService.ShowSuccessAsync(
                    "翻訳戦略選択",
                    $"{GetStrategyDisplayName(strategy)}に切り替えました。").ConfigureAwait(false);
            }

            _logger.LogInformation("Strategy successfully changed to {Strategy}", strategy);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation when selecting strategy {Strategy}", strategy);
            await _notificationService.ShowErrorAsync(
                "戦略選択エラー",
                $"選択できない翻訳戦略です: {ex.Message}").ConfigureAwait(false);
        }
        catch (NotSupportedException ex)
        {
            _logger.LogError(ex, "Unsupported strategy selected: {Strategy}", strategy);
            await _notificationService.ShowErrorAsync(
                "非対応戦略",
                $"この翻訳戦略はサポートされていません: {ex.Message}").ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not (InvalidOperationException or NotSupportedException))
        {
            _logger.LogError(ex, "Unexpected error when selecting strategy {Strategy}", strategy);
            await _notificationService.ShowErrorAsync(
                "戦略選択エラー",
                $"予期しないエラーが発生しました: {ex.Message}").ConfigureAwait(false);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// フォールバック設定切り替え処理
    /// </summary>
    private async Task ToggleFallbackAsync(bool enabled)
    {
        try
        {
            EnableFallback = enabled;

            var status = enabled ? "有効" : "無効";
            await _notificationService.ShowInfoAsync(
                "フォールバック設定",
                $"自動フォールバック機能を{status}にしました。").ConfigureAwait(false);

            _logger.LogInformation("Fallback setting changed to {Status}", status);
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "Invalid fallback setting value: {Enabled}", enabled);
            await _notificationService.ShowErrorAsync(
                "設定値エラー",
                "無効なフォールバック設定値です。").ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not ArgumentException)
        {
            _logger.LogError(ex, "Unexpected error when toggling fallback setting");
            await _notificationService.ShowErrorAsync(
                "設定変更エラー",
                $"予期しないエラーが発生しました: {ex.Message}").ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 戦略詳細表示
    /// </summary>
    private void ShowStrategyDetails(TranslationStrategy strategy)
    {
        var details = GetStrategyDetails(strategy);
        _notificationService.ShowInfoAsync(
            $"{GetStrategyDisplayName(strategy)}の詳細",
            details);
    }

    /// <summary>
    /// デフォルト設定リセット処理
    /// </summary>
    private async Task ResetToDefaultAsync()
    {
        try
        {
            IsLoading = true;

            var defaultStrategy = ParseStrategyFromString(_options.DefaultTranslationStrategy);
            var defaultFallback = true;

            SelectedStrategy = defaultStrategy;
            EnableFallback = defaultFallback;

            await _notificationService.ShowSuccessAsync(
                "設定リセット",
                "翻訳戦略設定をデフォルトに戻しました。").ConfigureAwait(false);

            _logger.LogInformation("Translation strategy settings reset to default");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation when resetting strategy settings");
            await _notificationService.ShowErrorAsync(
                "操作エラー",
                "現在の状態では設定をリセットできません。").ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogError(ex, "Unexpected error when resetting strategy settings");
            await _notificationService.ShowErrorAsync(
                "リセットエラー",
                $"予期しないエラーが発生しました: {ex.Message}").ConfigureAwait(false);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// 言語ペア変更時の処理
    /// </summary>
    private void OnLanguagePairChanged(string languagePair)
    {
        UpdateTwoStageAvailability();
        UpdateStrategyDescription();

        // 2段階翻訳が利用不可になった場合のフォールバック
        if (!IsTwoStageAvailable && SelectedStrategy == TranslationStrategy.TwoStage)
        {
            SelectedStrategy = TranslationStrategy.Direct;
            _logger.LogInformation("Fallback to Direct strategy due to language pair change: {Pair}", languagePair);
        }

        _logger.LogDebug("Language pair changed to {Pair}, TwoStage available: {Available}", 
            languagePair, IsTwoStageAvailable);
    }

    /// <summary>
    /// エンジン状態更新時の処理
    /// </summary>
    private void OnEngineStatusUpdate(TranslationEngineStatusUpdate update)
    {
        // 現在の戦略を再検証
        var warning = ValidateStrategyAsync(SelectedStrategy).ConfigureAwait(false).GetAwaiter().GetResult();
        
        if (!string.IsNullOrEmpty(warning))
        {
            HasStrategyWarning = true;
            StrategyWarningMessage = warning;
        }
        else
        {
            HasStrategyWarning = false;
            StrategyWarningMessage = string.Empty;
        }

        UpdateStrategyDescription();
        
        _logger.LogDebug("Strategy warning updated: {HasWarning}, Message: {Message}", 
            HasStrategyWarning, StrategyWarningMessage);
    }

    /// <summary>
    /// 2段階翻訳の利用可否を更新
    /// </summary>
    private void UpdateTwoStageAvailability()
    {
        // 日本語⇔中国語ペアでのみ2段階翻訳が利用可能
        IsTwoStageAvailable = CurrentLanguagePair switch
        {
            "ja-zh" or "ja-zh-Hans" or "ja-zh-Hant" => true,
            _ => false
        };
    }

    /// <summary>
    /// 戦略説明文の更新
    /// </summary>
    private void UpdateStrategyDescription()
    {
        SelectedStrategyDescription = SelectedStrategy switch
        {
            TranslationStrategy.Direct => GetDirectStrategyDescription(),
            TranslationStrategy.TwoStage => GetTwoStageStrategyDescription(),
            _ => "不明な翻訳戦略"
        };
    }

    /// <summary>
    /// 直接翻訳戦略の説明取得
    /// </summary>
    private string GetDirectStrategyDescription()
    {
        var baseDesc = "単一モデルによる直接翻訳。最高速度、最低レイテンシ。";
        
        if (HasStrategyWarning)
        {
            return $"{baseDesc}\n⚠️ {StrategyWarningMessage}";
        }
        
        return $"{baseDesc}\n✅ 推定レイテンシ: < 50ms";
    }

    /// <summary>
    /// 2段階翻訳戦略の説明取得
    /// </summary>
    private string GetTwoStageStrategyDescription()
    {
        if (!IsTwoStageAvailable)
        {
            return "2段階翻訳（中継言語経由）。高品質だが時間がかかる。\n❌ 現在の言語ペアでは利用できません。";
        }

        var baseDesc = "2段階翻訳（日本語→英語→中国語）。高品質だが時間がかかる。";
        
        if (HasStrategyWarning)
        {
            return $"{baseDesc}\n⚠️ {StrategyWarningMessage}";
        }
        
        return $"{baseDesc}\n✅ 推定レイテンシ: < 100ms";
    }

    /// <summary>
    /// 戦略の妥当性検証
    /// </summary>
    private async Task<string> ValidateStrategyAsync(TranslationStrategy strategy)
    {
        await Task.CompletedTask.ConfigureAwait(false); // 非同期処理の準備

        if (strategy == TranslationStrategy.TwoStage)
        {
            // 2段階翻訳は中間エンジンとターゲットエンジンの両方が必要
            if (!LocalEngineStatus.IsHealthy)
            {
                return "2段階翻訳にはLocalOnlyエンジンが正常である必要があります";
            }

            if (!IsTwoStageAvailable)
            {
                return "現在の言語ペアでは2段階翻訳は利用できません";
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// 利用可能戦略リストの作成
    /// </summary>
    private static IReadOnlyList<TranslationStrategyItem> CreateAvailableStrategiesList()
    {
        return [
            new(
                TranslationStrategy.Direct,
                "Direct",
                "直接翻訳",
                "単一モデルによる高速翻訳"),
            new(
                TranslationStrategy.TwoStage,
                "TwoStage",
                "2段階翻訳",
                "中継言語経由の高品質翻訳（ja-zh専用）")
        ];
    }

    /// <summary>
    /// 戦略表示名の取得
    /// </summary>
    private static string GetStrategyDisplayName(TranslationStrategy strategy)
    {
        return strategy switch
        {
            TranslationStrategy.Direct => "直接翻訳",
            TranslationStrategy.TwoStage => "2段階翻訳",
            _ => "不明な戦略"
        };
    }

    /// <summary>
    /// 戦略詳細情報の取得
    /// </summary>
    private static string GetStrategyDetails(TranslationStrategy strategy)
    {
        return strategy switch
        {
            TranslationStrategy.Direct => 
                "【直接翻訳】\n" +
                "• 単一の翻訳モデルを使用\n" +
                "• 最高速度（< 50ms）\n" +
                "• 全言語ペア対応\n" +
                "• メモリ使用量: 最小\n" +
                "• 推奨用途: 日常的な翻訳、リアルタイム翻訳",

            TranslationStrategy.TwoStage => 
                "【2段階翻訳】\n" +
                "• 中継言語（英語）経由の翻訳\n" +
                "• 高品質（< 100ms）\n" +
                "• ja-zh言語ペア専用\n" +
                "• メモリ使用量: 中程度\n" +
                "• 推奨用途: 高品質が必要な文書翻訳",

            _ => "詳細情報が利用できません。"
        };
    }

    /// <summary>
    /// 文字列から戦略をパース
    /// </summary>
    private static TranslationStrategy ParseStrategyFromString(string strategyString)
    {
        return strategyString?.ToUpperInvariant() switch
        {
            "DIRECT" => TranslationStrategy.Direct,
            "TWOSTAGE" => TranslationStrategy.TwoStage,
            _ => TranslationStrategy.Direct // デフォルト
        };
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _disposables?.Dispose();
    }
}

/// <summary>
/// 翻訳戦略選択項目
/// </summary>
public sealed record TranslationStrategyItem(
    TranslationStrategy Strategy,
    string Id,
    string DisplayName,
    string Description);
