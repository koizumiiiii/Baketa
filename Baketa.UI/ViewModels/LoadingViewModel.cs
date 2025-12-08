using System.Collections.ObjectModel;
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
/// </summary>
public class LoadingViewModel : ViewModelBase
{
    private readonly ILoadingScreenInitializer _initializer;
    private readonly ILogger<LoadingViewModel> _logger;
    private bool _disposed;

    /// <summary>
    /// 初期化ステップのコレクション
    /// </summary>
    public ObservableCollection<InitializationStep> InitializationSteps { get; }

    /// <summary>
    /// バージョン情報テキスト
    /// </summary>
    public string VersionText { get; }

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

        // 初期化ステップを作成（ローカライズリソースを使用）
        // [Issue #185] ダウンロードステップを先頭に追加
        InitializationSteps =
        [
            new("download_components", Strings.Loading_DownloadingComponents),
            new("resolve_dependencies", Strings.Loading_ResolvingDependencies),
            new("load_ocr", Strings.Loading_LoadingOCR),
            new("init_translation", Strings.Loading_InitializingTranslation),
            new("prepare_ui", Strings.Loading_PreparingUI)
        ];

        // 進捗イベントを購読
        _initializer.ProgressChanged += OnProgressChanged;
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
            }
            else
            {
                _logger.LogWarning("[Issue185] 対応するステップが見つかりません: {StepId}", e.StepId);
            }
        });
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
    }
}
