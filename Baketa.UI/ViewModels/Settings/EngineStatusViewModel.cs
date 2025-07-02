using System;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReactiveUI;
using Baketa.Core.Abstractions.Events;
using Baketa.UI.Configuration;
using Baketa.UI.Framework;
using Baketa.UI.Services;

namespace Baketa.UI.ViewModels.Settings;

/// <summary>
/// エンジン状態表示ViewModel
/// </summary>
public sealed class EngineStatusViewModel : Framework.ViewModelBase, IActivatableViewModel
{
    private readonly ITranslationEngineStatusService _statusService;
    private readonly ILogger<EngineStatusViewModel> _logger;
    private readonly TranslationUIOptions _options;
    private readonly CompositeDisposable _disposables = [];

    private string _localEngineStatusText = string.Empty;
    private string _cloudEngineStatusText = string.Empty;
    private bool _isLocalEngineHealthy;
    private bool _isCloudEngineHealthy;
    private string _lastUpdateTime = string.Empty;

    /// <summary>
    /// ViewModel活性化管理
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CS0109:Member does not hide an accessible member", Justification = "Intentional shadowing for independent activation management")]
    public new ViewModelActivator Activator { get; } = new();

    /// <summary>
    /// LocalOnlyエンジンの状態テキスト
    /// </summary>
    public string LocalEngineStatusText
    {
        get => _localEngineStatusText;
        private set => this.RaiseAndSetIfChanged(ref _localEngineStatusText, value);
    }

    /// <summary>
    /// CloudOnlyエンジンの状態テキスト
    /// </summary>
    public string CloudEngineStatusText
    {
        get => _cloudEngineStatusText;
        private set => this.RaiseAndSetIfChanged(ref _cloudEngineStatusText, value);
    }

    /// <summary>
    /// LocalOnlyエンジンが正常かどうか
    /// </summary>
    public bool IsLocalEngineHealthy
    {
        get => _isLocalEngineHealthy;
        private set => this.RaiseAndSetIfChanged(ref _isLocalEngineHealthy, value);
    }

    /// <summary>
    /// CloudOnlyエンジンが正常かどうか
    /// </summary>
    public bool IsCloudEngineHealthy
    {
        get => _isCloudEngineHealthy;
        private set => this.RaiseAndSetIfChanged(ref _isCloudEngineHealthy, value);
    }

    /// <summary>
    /// 最終更新時刻
    /// </summary>
    public string LastUpdateTime
    {
        get => _lastUpdateTime;
        private set => this.RaiseAndSetIfChanged(ref _lastUpdateTime, value);
    }

    /// <summary>
    /// 状態更新コマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> RefreshStatusCommand { get; }

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public EngineStatusViewModel(
        ITranslationEngineStatusService statusService,
        IOptions<TranslationUIOptions> options,
        ILogger<EngineStatusViewModel> logger,
        IEventAggregator eventAggregator) : base(eventAggregator)
    {
        ArgumentNullException.ThrowIfNull(statusService);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);
        
        _statusService = statusService;
        _logger = logger;
        _options = options.Value;

        // コマンドの作成
        RefreshStatusCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            await _statusService.RefreshStatusAsync().ConfigureAwait(false);
            UpdateStatusDisplay();
        });

        // ViewModel活性化時の処理
        this.WhenActivated(disposables =>
        {
            // エンジン状態更新の監視
            _statusService.StatusUpdates
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ => UpdateStatusDisplay())
                .DisposeWith(disposables);

            // 初期状態更新
            UpdateStatusDisplay();

            _logger.LogDebug("EngineStatusViewModel activated");
        });

        _logger.LogInformation("EngineStatusViewModel created");
    }

    /// <summary>
    /// 状態表示の更新
    /// </summary>
    private void UpdateStatusDisplay()
    {
        var localStatus = _statusService.LocalEngineStatus;
        var cloudStatus = _statusService.CloudEngineStatus;

        // LocalOnlyエンジンの状態
        IsLocalEngineHealthy = localStatus.IsHealthy;
        LocalEngineStatusText = localStatus.IsHealthy ? 
            "正常" : 
            $"エラー: {localStatus.LastError}";

        // CloudOnlyエンジンの状態
        IsCloudEngineHealthy = cloudStatus.IsHealthy && cloudStatus.IsOnline;
        CloudEngineStatusText = !cloudStatus.IsOnline ? 
            "オフライン" : 
            cloudStatus.IsHealthy ? 
                $"正常 (残り: {cloudStatus.RemainingRequests}回)" : 
                $"エラー: {cloudStatus.LastError}";

        LastUpdateTime = DateTime.Now.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

        _logger.LogDebug("Engine status display updated - Local: {LocalHealthy}, Cloud: {CloudHealthy}", 
            IsLocalEngineHealthy, IsCloudEngineHealthy);
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
