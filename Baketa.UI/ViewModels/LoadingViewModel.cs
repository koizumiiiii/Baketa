using System.Collections.ObjectModel;
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

namespace Baketa.UI.ViewModels;

/// <summary>
/// ローディング画面用ViewModel
/// 初期化ステップの進捗を管理します
/// [Issue #193] GPU環境自動セットアップステップ追加
/// [Issue #259] UI簡素化 - 統合プログレスバー + Tips機能追加
/// </summary>
public class LoadingViewModel : ViewModelBase
{
    private readonly ILoadingScreenInitializer _initializer;
    private readonly ILogger<LoadingViewModel> _logger;
    private readonly IDisposable _tipRotationSubscription;
    private bool _disposed;

    // [Issue #259] 統合プログレス用プロパティ
    private string _currentStepMessage = string.Empty;
    private int _overallProgress;
    private bool _isIndeterminate = true;
    private string _currentTip = string.Empty;
    private int _currentTipIndex;

    /// <summary>
    /// [Issue #259] ステップごとの重み（合計100%）
    /// </summary>
    private static readonly Dictionary<string, int> StepWeights = new()
    {
        ["download_components"] = 30,   // モデルダウンロード（最大）
        ["setup_gpu"] = 10,             // GPU環境セットアップ
        ["resolve_dependencies"] = 15,  // 依存関係解決
        ["load_ocr"] = 20,              // OCRエンジン読み込み
        ["init_translation"] = 15,      // 翻訳エンジン初期化
        ["prepare_ui"] = 10,            // UI準備
        // [Issue #507] フェーズグループ通知（重み0: 個別ステップで進捗管理するため）
        ["parallel_init"] = 0,          // Phase 1 開始通知
        ["parallel_engines"] = 0        // Phase 3 開始通知
    };

    /// <summary>
    /// [Issue #259][Issue #475] Tips文字列リスト（多言語ローテーション対応）
    /// 選択言語の反映が不安定なため、対応全言語のTipsをローテーション表示
    /// </summary>
    private readonly string[] _tips;

    /// <summary>
    /// 初期化ステップのコレクション（内部管理用）
    /// </summary>
    private ObservableCollection<InitializationStep> InitializationSteps { get; }

    /// <summary>
    /// バージョン情報テキスト
    /// </summary>
    public string VersionText { get; }

    /// <summary>
    /// [Issue #259] 現在のステップメッセージ
    /// </summary>
    public string CurrentStepMessage
    {
        get => _currentStepMessage;
        private set => this.RaiseAndSetIfChanged(ref _currentStepMessage, value);
    }

    /// <summary>
    /// [Issue #259] 統合プログレス（0-100）
    /// </summary>
    public int OverallProgress
    {
        get => _overallProgress;
        private set => this.RaiseAndSetIfChanged(ref _overallProgress, value);
    }

    /// <summary>
    /// [Issue #259] 不確定プログレスモード
    /// </summary>
    public bool IsIndeterminate
    {
        get => _isIndeterminate;
        private set => this.RaiseAndSetIfChanged(ref _isIndeterminate, value);
    }

    /// <summary>
    /// [Issue #259] 現在のTipテキスト
    /// </summary>
    public string CurrentTip
    {
        get => _currentTip;
        private set => this.RaiseAndSetIfChanged(ref _currentTip, value);
    }

    public LoadingViewModel(
        IEventAggregator eventAggregator,
        ILoadingScreenInitializer initializer,
        ILogger<LoadingViewModel> logger)
        : base(eventAggregator)
    {
        _initializer = initializer ?? throw new ArgumentNullException(nameof(initializer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // バージョン情報を取得（MinVerはInformationalVersionに設定する）
        var assembly = Assembly.GetExecutingAssembly();
        var infoVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        // InformationalVersionは "0.2.14-alpha.0.13+build" のような形式になる場合がある
        // 表示用にはメジャー.マイナー.パッチのみを抽出（ハイフンや+以降を除去）
        var versionDisplay = infoVersion?.Split(['-', '+'])[0] ?? assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        VersionText = $"Version {versionDisplay}";

        // 初期化ステップを作成（内部管理用）
        // [Issue #185] ダウンロードステップを先頭に追加
        // [Issue #193] GPU環境セットアップステップを追加
        InitializationSteps =
        [
            new("download_components", Strings.Loading_DownloadingComponents),
            new("setup_gpu", Strings.Loading_SetupGpu),
            new("resolve_dependencies", Strings.Loading_ResolvingDependencies),
            new("load_ocr", Strings.Loading_LoadingOCR),
            new("init_translation", Strings.Loading_InitializingTranslation),
            new("prepare_ui", Strings.Loading_PreparingUI),
            // [Issue #507] フェーズグループ通知ステップ（UIには表示されないが、進捗イベント受信用）
            new("parallel_init", Strings.Loading_ResolvingDependencies),
            new("parallel_engines", Strings.Loading_LoadingOCR)
        ];

        // [Issue #259][Issue #475] Tips文字列を初期化（多言語ローテーション）
        _tips = LoadTips(_logger);
        _currentTip = _tips.Length > 0 ? _tips[0] : string.Empty;
        _logger.LogInformation("[Issue #475] Tips多言語ローテーション: {Count}個ロード済み", _tips.Length);

        // [Issue #475] Tips自動ローテーション（5秒ごと）
        // LoadingWindowはReactiveWindowではないためHandleActivationが呼ばれない
        // コンストラクタで直接タイマーを開始する
        _tipRotationSubscription = Observable.Interval(TimeSpan.FromSeconds(5))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => RotateTip());

        // 進捗イベントを購読
        _initializer.ProgressChanged += OnProgressChanged;
    }

    /// <summary>
    /// [Issue #475] 対応全言語のカルチャ一覧（resxが存在する言語）
    /// </summary>
    private static readonly System.Globalization.CultureInfo[] TipCultures =
    [
        new("en"), new("ja"), new("zh-CN"), new("zh-TW"), new("ko"),
        new("es"), new("fr"), new("de"), new("it"), new("pt")
    ];

    /// <summary>
    /// [Issue #259][Issue #475] リソースファイルからTipsを読み込む（多言語ローテーション対応）
    /// 選択言語の反映が不安定なため、同じTipを全対応言語で順番に表示する
    /// 例: Tip1(en) → Tip1(ja) → Tip1(zh-CN) → ... → Tip2(en) → Tip2(ja) → ...
    /// </summary>
    private static string[] LoadTips(ILogger logger)
    {
        var resourceManager = Strings.ResourceManager;
        var tips = new List<string>();
        var seen = new HashSet<string>(); // 同一テキストの重複排除

        for (var i = 1; i <= 10; i++)
        {
            var englishTip = resourceManager.GetString($"Loading_Tip_{i}", TipCultures[0]);
            if (string.IsNullOrEmpty(englishTip))
                break;

            foreach (var culture in TipCultures)
            {
                var tip = resourceManager.GetString($"Loading_Tip_{i}", culture);
                if (!string.IsNullOrEmpty(tip) && seen.Add(tip))
                {
                    tips.Add(tip);
                }
                else if (string.IsNullOrEmpty(tip))
                {
                    logger.LogDebug("[Issue #475] Tip_{Index} missing for culture {Culture}", i, culture.Name);
                }
            }
        }

        logger.LogDebug("[Issue #475] LoadTips結果: {Total}個（重複排除後）, カルチャ数: {CultureCount}",
            tips.Count, TipCultures.Length);

        return tips.Count > 0
            ? [.. tips]
            : ["Baketa - Game Translation Overlay"];
    }

    /// <summary>
    /// [Issue #259] Tipをローテーション
    /// </summary>
    private void RotateTip()
    {
        if (_tips.Length == 0) return;
        _currentTipIndex = (_currentTipIndex + 1) % _tips.Length;
        CurrentTip = _tips[_currentTipIndex];
    }

    /// <summary>
    /// 初期化進捗イベントハンドラ
    /// </summary>
    private void OnProgressChanged(object? sender, LoadingProgressEventArgs e)
    {
        // [Issue #185] デバッグログ: 進捗イベント受信確認
        _logger.LogDebug("[Issue185] OnProgressChanged受信: StepId={StepId}, Progress={Progress}%, IsCompleted={IsCompleted}",
            e.StepId, e.Progress, e.IsCompleted);
        _logger.LogDebug("[Issue185] Message: {Message}", e.Message);

        // [Issue #185] UIスレッドへディスパッチしてプロパティ更新
        // バックグラウンドスレッドからの呼び出しでもUIが正しく更新されるようにする
        Dispatcher.UIThread.Post(() =>
        {
            var step = InitializationSteps.FirstOrDefault(s => s.StepId == e.StepId);
            if (step != null)
            {
                // [Issue #185] 詳細メッセージ（ダウンロード進捗など）も渡す
                step.Update(e.IsCompleted, e.Progress, e.Message);
                _logger.LogDebug("[Issue185] ステップ更新完了: {StepId}, DetailMessage: {DetailMessage}", e.StepId, e.Message);

                // [Issue #259] 統合プログレスを更新
                UpdateOverallProgress(step, e);
            }
            else
            {
                _logger.LogWarning("[Issue185] 対応するステップが見つかりません: {StepId}", e.StepId);
            }
        });
    }

    /// <summary>
    /// [Issue #259] 統合プログレスを計算・更新
    /// </summary>
    private void UpdateOverallProgress(InitializationStep currentStep, LoadingProgressEventArgs e)
    {
        // 現在のステップメッセージを更新
        CurrentStepMessage = !string.IsNullOrEmpty(e.Message) ? e.Message : currentStep.Message;

        // 重み付き進捗を計算
        var totalProgress = 0;
        var stepIndex = 0;
        var currentStepFound = false;

        foreach (var step in InitializationSteps)
        {
            if (!StepWeights.TryGetValue(step.StepId, out var weight))
            {
                weight = 10; // デフォルト重み
            }

            if (step.IsCompleted)
            {
                // 完了したステップは100%寄与
                totalProgress += weight;
            }
            else if (step.StepId == e.StepId)
            {
                // 現在のステップは進捗率に応じて寄与
                totalProgress += (weight * e.Progress) / 100;
                currentStepFound = true;
            }
            // 未開始のステップは0%寄与

            stepIndex++;
        }

        OverallProgress = totalProgress;

        // 不確定モードの判定（進捗が0で実行中の場合）
        IsIndeterminate = !currentStepFound || (e.Progress == 0 && !e.IsCompleted);

        _logger.LogDebug("[Issue259] 統合プログレス: {OverallProgress}%, IsIndeterminate: {IsIndeterminate}",
            OverallProgress, IsIndeterminate);
    }

    /// <summary>
    /// リソース解放
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            _tipRotationSubscription.Dispose();
            _initializer.ProgressChanged -= OnProgressChanged;
        }

        _disposed = true;
        base.Dispose(disposing);
    }
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
    /// [Issue #185] 詳細メッセージ（動的更新用）
    /// ダウンロード進捗など、リアルタイムで更新される情報を表示
    /// </summary>
    public string DetailMessage
    {
        get => _detailMessage;
        private set => this.RaiseAndSetIfChanged(ref _detailMessage, value);
    }

    /// <summary>
    /// [Issue #185] 詳細メッセージが存在するか
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
    /// 不確定進捗モード（進捗が0で実行中の場合）
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
    /// [Issue #185] ステップの状態を詳細メッセージ付きで更新
    /// [Issue #189] IsInProgress条件を修正: progress > 0 の条件を削除
    /// </summary>
    public void Update(bool isCompleted, int progress, string? detailMessage)
    {
        IsCompleted = isCompleted;
        // Issue #189: ステップ開始時（progress=0）でもスピナーを表示するように修正
        IsInProgress = !isCompleted;
        Progress = progress;

        // [Issue #185] 詳細メッセージを更新（完了時はクリア）
        DetailMessage = isCompleted ? string.Empty : (detailMessage ?? string.Empty);

        // StatusIconとStatusColorの変更通知
        this.RaisePropertyChanged(nameof(StatusIcon));
        this.RaisePropertyChanged(nameof(StatusColor));
        this.RaisePropertyChanged(nameof(HasDetailMessage));
        this.RaisePropertyChanged(nameof(IsIndeterminate));
    }
}
