using System.Collections.ObjectModel;
using System.Globalization;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reflection;
using Avalonia.Threading;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Services;
using Baketa.UI.Framework;
using Baketa.UI.Resources;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Baketa.UI.ViewModels;

/// <summary>
/// ローディング画面用ViewModel
/// [Issue #259] UI簡素化 + Tips機能追加
/// </summary>
public class LoadingViewModel : ViewModelBase
{
    private readonly ILoadingScreenInitializer _initializer;
    private readonly ILogger<LoadingViewModel> _logger;
    private readonly ObservableCollection<InitializationStep> _steps;
    private readonly List<LoadingTip> _tips;
    private int _currentTipIndex;
    private bool _disposed;

    #region 表示用プロパティ（Issue #259: UI簡素化）

    /// <summary>
    /// 現在のステップメッセージ
    /// </summary>
    [Reactive]
    public string CurrentStepMessage { get; set; } = string.Empty;

    /// <summary>
    /// 全体進捗率 (0-100)
    /// </summary>
    [Reactive]
    public int OverallProgress { get; set; }

    /// <summary>
    /// 不確定進捗モード
    /// </summary>
    [Reactive]
    public bool IsIndeterminate { get; set; } = true;

    /// <summary>
    /// 現在表示中のTips
    /// </summary>
    [Reactive]
    public string CurrentTip { get; set; } = string.Empty;

    /// <summary>
    /// バージョン情報テキスト
    /// </summary>
    public string VersionText { get; }

    #endregion

    #region 後方互換性（内部使用）

    /// <summary>
    /// 初期化ステップのコレクション（内部管理用）
    /// UI表示には使用しないが、進捗計算に使用
    /// </summary>
    public ObservableCollection<InitializationStep> InitializationSteps => _steps;

    #endregion

    #region 設定値（Issue #259）

    /// <summary>
    /// Tipsローテーション間隔（秒）
    /// </summary>
    private const int TipRotationIntervalSeconds = 5;

    /// <summary>
    /// 各ステップの重み（合計100）
    /// 初回ダウンロードは時間がかかるため重く設定
    /// </summary>
    private static readonly Dictionary<string, int> StepWeights = new()
    {
        ["download_components"] = 40,   // 初回は重い
        ["setup_gpu"] = 10,
        ["resolve_dependencies"] = 10,
        ["load_ocr"] = 15,
        ["init_translation"] = 15,
        ["prepare_ui"] = 10
    };

    #endregion

    public LoadingViewModel(
        IEventAggregator eventAggregator,
        ILoadingScreenInitializer initializer,
        ILogger<LoadingViewModel> logger)
        : base(eventAggregator)
    {
        _initializer = initializer ?? throw new ArgumentNullException(nameof(initializer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // バージョン情報を取得
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText = $"Version {version?.ToString(3) ?? "0.0.0"}";

        // 初期化ステップを作成（内部管理用）
        _steps =
        [
            new("download_components", Strings.Loading_DownloadingComponents),
            new("setup_gpu", Strings.Loading_SetupGpu),
            new("resolve_dependencies", Strings.Loading_ResolvingDependencies),
            new("load_ocr", Strings.Loading_LoadingOCR),
            new("init_translation", Strings.Loading_InitializingTranslation),
            new("prepare_ui", Strings.Loading_PreparingUI)
        ];

        // Tips定義（Issue #259）
        _tips = InitializeTips();

        // 初期Tipを設定
        if (_tips.Count > 0)
        {
            CurrentTip = GetLocalizedTip(_tips[0]);
        }

        // WhenActivatedでライフサイクル管理を統一
        this.WhenActivated(disposables =>
        {
            // 進捗イベントを購読（Dispose時に自動解除）
            _initializer.ProgressChanged += OnProgressChanged;
            Disposable.Create(() => _initializer.ProgressChanged -= OnProgressChanged)
                .DisposeWith(disposables);

            // Observable.IntervalでTipsをローテーション
            Observable.Interval(TimeSpan.FromSeconds(TipRotationIntervalSeconds), RxApp.MainThreadScheduler)
                .Subscribe(_ => RotateToNextTip())
                .DisposeWith(disposables);

            _logger.LogDebug("[Issue259] Tips rotation timer started");
        });
    }

    #region Tips機能（Issue #259）

    /// <summary>
    /// Tips定義を初期化
    /// </summary>
    private static List<LoadingTip> InitializeTips()
    {
        return
        [
            new LoadingTip(
                Strings.Loading_Tip_FirstLaunch_Ja,
                Strings.Loading_Tip_FirstLaunch_En),
            new LoadingTip(
                Strings.Loading_Tip_About_Ja,
                Strings.Loading_Tip_About_En),
            new LoadingTip(
                Strings.Loading_Tip_LiveMode_Ja,
                Strings.Loading_Tip_LiveMode_En),
            new LoadingTip(
                Strings.Loading_Tip_SingleshotMode_Ja,
                Strings.Loading_Tip_SingleshotMode_En)
        ];
    }

    /// <summary>
    /// 次のTipに切り替え
    /// </summary>
    private void RotateToNextTip()
    {
        if (_tips.Count == 0) return;

        _currentTipIndex = (_currentTipIndex + 1) % _tips.Count;
        CurrentTip = GetLocalizedTip(_tips[_currentTipIndex]);
        _logger.LogDebug("[Issue259] Tip rotated to index {Index}", _currentTipIndex);
    }

    /// <summary>
    /// 言語に応じたTipメッセージを取得
    /// </summary>
    private static string GetLocalizedTip(LoadingTip tip)
    {
        var culture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return culture == "ja" ? tip.MessageJa : tip.MessageEn;
    }

    #endregion

    #region 進捗管理

    /// <summary>
    /// 初期化進捗イベントハンドラ
    /// </summary>
    private void OnProgressChanged(object? sender, LoadingProgressEventArgs e)
    {
        _logger.LogDebug("[Issue259] OnProgressChanged: StepId={StepId}, Progress={Progress}%, IsCompleted={IsCompleted}",
            e.StepId, e.Progress, e.IsCompleted);

        Dispatcher.UIThread.Post(() =>
        {
            var step = _steps.FirstOrDefault(s => s.StepId == e.StepId);
            if (step != null)
            {
                // ステップの状態を更新
                step.Update(e.IsCompleted, e.Progress, e.Message);

                // 現在のステップメッセージを更新
                if (!e.IsCompleted)
                {
                    CurrentStepMessage = step.Message;
                    if (!string.IsNullOrEmpty(e.Message))
                    {
                        CurrentStepMessage = e.Message; // 詳細メッセージがあればそれを表示
                    }
                }

                // 全体進捗を再計算
                UpdateOverallProgress();

                _logger.LogDebug("[Issue259] Overall progress updated: {Progress}%", OverallProgress);
            }
            else
            {
                _logger.LogWarning("[Issue259] Unknown step: {StepId}", e.StepId);
            }
        });
    }

    /// <summary>
    /// 全体進捗を計算（Issue #259: 重み付け計算）
    /// </summary>
    private void UpdateOverallProgress()
    {
        // 完了済みステップの重みを合計
        var completedWeight = _steps
            .Where(s => s.IsCompleted)
            .Sum(s => StepWeights.GetValueOrDefault(s.StepId, 10));

        // 実行中ステップの部分進捗
        var inProgressStep = _steps.FirstOrDefault(s => s.IsInProgress);
        var inProgressWeight = 0;
        if (inProgressStep != null)
        {
            var stepWeight = StepWeights.GetValueOrDefault(inProgressStep.StepId, 10);
            inProgressWeight = stepWeight * inProgressStep.Progress / 100;
        }

        var newProgress = completedWeight + inProgressWeight;
        OverallProgress = Math.Min(newProgress, 100);

        // 進捗が開始されたら不確定モードを解除
        IsIndeterminate = OverallProgress == 0 && !_steps.Any(s => s.IsInProgress);
    }

    #endregion

    #region Dispose

    /// <summary>
    /// リソース解放
    /// </summary>
    /// <remarks>
    /// 進捗イベントのアンサブスクライブはWhenActivated内で自動管理されるため、
    /// ここでは追加のクリーンアップは不要
    /// </remarks>
    protected override void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        _disposed = true;
        base.Dispose(disposing);
    }

    #endregion
}

/// <summary>
/// 初期化ステップの表示用モデル
/// </summary>
public class InitializationStep : ReactiveObject
{
    private bool _isCompleted;
    private bool _isInProgress;
    private int _progress;
    private string _detailMessage = string.Empty;

    /// <summary>
    /// ステップID
    /// </summary>
    public string StepId { get; }

    /// <summary>
    /// 表示メッセージ（静的）
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// 詳細メッセージ（動的更新用）
    /// </summary>
    public string DetailMessage
    {
        get => _detailMessage;
        private set => this.RaiseAndSetIfChanged(ref _detailMessage, value);
    }

    /// <summary>
    /// 詳細メッセージが存在するか
    /// </summary>
    public bool HasDetailMessage => !string.IsNullOrEmpty(DetailMessage);

    /// <summary>
    /// 完了状態
    /// </summary>
    public bool IsCompleted
    {
        get => _isCompleted;
        private set => this.RaiseAndSetIfChanged(ref _isCompleted, value);
    }

    /// <summary>
    /// 実行中状態
    /// </summary>
    public bool IsInProgress
    {
        get => _isInProgress;
        private set => this.RaiseAndSetIfChanged(ref _isInProgress, value);
    }

    /// <summary>
    /// 進捗率 (0-100)
    /// </summary>
    public int Progress
    {
        get => _progress;
        private set => this.RaiseAndSetIfChanged(ref _progress, value);
    }

    /// <summary>
    /// 不確定進捗モード
    /// </summary>
    public bool IsIndeterminate => IsInProgress && Progress == 0;

    /// <summary>
    /// ステータスアイコン
    /// </summary>
    public string StatusIcon => IsCompleted ? "✓" : (IsInProgress ? "●" : "○");

    /// <summary>
    /// ステータスカラー
    /// </summary>
    public string StatusColor => IsCompleted ? "#10B981" : (IsInProgress ? "#3B82F6" : "#52525B");

    public InitializationStep(string stepId, string message)
    {
        StepId = stepId;
        Message = message;
        IsCompleted = false;
        IsInProgress = false;
        Progress = 0;
    }

    /// <summary>
    /// ステップの状態を更新
    /// </summary>
    public void Update(bool isCompleted, int progress)
    {
        Update(isCompleted, progress, null);
    }

    /// <summary>
    /// ステップの状態を詳細メッセージ付きで更新
    /// </summary>
    public void Update(bool isCompleted, int progress, string? detailMessage)
    {
        IsCompleted = isCompleted;
        IsInProgress = !isCompleted;
        Progress = progress;
        DetailMessage = isCompleted ? string.Empty : (detailMessage ?? string.Empty);

        this.RaisePropertyChanged(nameof(StatusIcon));
        this.RaisePropertyChanged(nameof(StatusColor));
        this.RaisePropertyChanged(nameof(HasDetailMessage));
        this.RaisePropertyChanged(nameof(IsIndeterminate));
    }
}

/// <summary>
/// Tips定義（Issue #259）
/// </summary>
/// <param name="MessageJa">日本語メッセージ</param>
/// <param name="MessageEn">英語メッセージ</param>
public record LoadingTip(string MessageJa, string MessageEn);
