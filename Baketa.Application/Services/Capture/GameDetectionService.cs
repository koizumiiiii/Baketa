using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Events.CaptureEvents;
using Baketa.Core.Events;
using Baketa.Core.Settings;
using CoreEventAggregator = Baketa.Core.Abstractions.Events.IEventAggregator;
using SettingsGameCaptureProfile = Baketa.Core.Settings.GameCaptureProfile;

namespace Baketa.Application.Services.Capture;

/// <summary>
/// ゲーム自動検出・プロファイル適用サービス
/// </summary>
public interface IGameDetectionService
{
    /// <summary>
    /// ゲーム自動検出を開始します
    /// </summary>
    Task StartDetectionAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// ゲーム自動検出を停止します
    /// </summary>
    Task StopDetectionAsync();
    
    /// <summary>
    /// 検出が実行中かどうか
    /// </summary>
    bool IsDetectionRunning { get; }
}

/// <summary>
/// ゲーム自動検出・プロファイル適用サービスの実装
/// </summary>
public partial class GameDetectionService : IGameDetectionService, IDisposable
{
    private readonly IGameProfileManager _profileManager;
    private readonly IAdvancedCaptureService _captureService;
    private readonly CoreEventAggregator _eventAggregator;
    private readonly ILogger<GameDetectionService>? _logger;
    
    private System.Threading.Timer? _detectionTimer;
    private string? _lastDetectedGame;
    private SettingsGameCaptureProfile? _currentProfile;
    private readonly object _syncLock = new();
    
    public bool IsDetectionRunning { get; private set; }
    
    public GameDetectionService(
        IGameProfileManager profileManager,
        IAdvancedCaptureService captureService,
        CoreEventAggregator eventAggregator,
        ILogger<GameDetectionService>? logger = null)
    {
        _profileManager = profileManager ?? throw new ArgumentNullException(nameof(profileManager));
        _captureService = captureService ?? throw new ArgumentNullException(nameof(captureService));
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _logger = logger;
    }
    
    public Task StartDetectionAsync(CancellationToken cancellationToken = default)
    {
        lock (_syncLock)
        {
            if (IsDetectionRunning)
            {
                _logger?.LogWarning("ゲーム検出は既に実行中です");
                return Task.CompletedTask;
            }
            
            _detectionTimer = new System.Threading.Timer(DetectGameCallback, null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
            IsDetectionRunning = true;
            
            _logger?.LogInformation("ゲーム自動検出を開始しました");
        }
        
        return Task.CompletedTask;
    }
    
    public Task StopDetectionAsync()
    {
        lock (_syncLock)
        {
            if (!IsDetectionRunning)
                return Task.CompletedTask;
            
            // Timer.Dispose()には非同期バージョンが存在しないため同期実行
            #pragma warning disable CA1849 // Call async methods when in an async method
            _detectionTimer?.Dispose();
            #pragma warning restore CA1849
            _detectionTimer = null;
            IsDetectionRunning = false;
            
            _logger?.LogInformation("ゲーム自動検出を停止しました");
        }
        
        return Task.CompletedTask;
    }
    
    /// <summary>
    /// タイマーコールバックメソッド（async voidの使用は必要）
    /// </summary>
    /// <param name="state">タイマー状態</param>
    #pragma warning disable VSTHRD100 // Avoid async void methods
    private async void DetectGameCallback(object? state)
    {
        try
        {
            await DetectAndApplyGameProfileAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException ex)
        {
            _logger?.LogWarning(ex, "オブジェクトが既に破棄されています（サービス停止中）");
            // サービスが停止中の場合は正常な状況
        }
        catch (ArgumentException ex)
        {
            _logger?.LogError(ex, "プロセス引数エラーが発生しました");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger?.LogError(ex, "プロセスアクセス権限エラーが発生しました");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _logger?.LogError(ex, "Windows APIエラーが発生しました");
        }
        catch (TimeoutException ex)
        {
            _logger?.LogError(ex, "ゲーム検出処理中にタイムアウトが発生しました");
        }
        catch (InvalidOperationException ex)
        {
            _logger?.LogError(ex, "ゲーム検出処理中にエラーが発生しました");
        }
        // 最後の手段として一般的な例外をキャッチ（致命的例外を除く）
        catch (Exception ex) when (!(ex is OutOfMemoryException || ex is StackOverflowException))
        {
            _logger?.LogError(ex, "予期しないエラーが発生しました: {ExceptionType}", ex.GetType().Name);
        }
    }
    #pragma warning restore VSTHRD100 // Avoid async void methods
    
    private async Task DetectAndApplyGameProfileAsync()
    {
        try
        {
            // アクティブプロセスの取得
            var activeProcess = GetActiveProcess();
            if (activeProcess == null)
                return;
            
            string processName = activeProcess.ProcessName;
            string? windowTitle = activeProcess.MainWindowTitle;
            
            // 前回と同じゲームの場合はスキップ
            string gameId = $"{processName}|{windowTitle}";
            if (_lastDetectedGame == gameId)
                return;
            
            _logger?.LogDebug("アクティブプロセス検出: {ProcessName} - {WindowTitle}", processName, windowTitle);
            
            // 適合するプロファイルを検索
            var matchingProfile = await _profileManager.FindMatchingProfileAsync(processName, windowTitle).ConfigureAwait(false);
            
            if (matchingProfile != null && !ReferenceEquals(matchingProfile, _currentProfile))
            {
                _logger?.LogInformation("ゲーム '{GameName}' を検出、プロファイル '{ProfileName}' を適用", 
                    processName, matchingProfile.Name);
                
                // プロファイルを適用（軽量な設定変更処理のため同期実行）
                #pragma warning disable CA1849 // Call async methods when in an async method
                _captureService.ApplyGameProfile(matchingProfile);
                #pragma warning restore CA1849
                _currentProfile = matchingProfile;
                
                // イベント発行
                await PublishGameDetectedEventAsync(processName, windowTitle, matchingProfile).ConfigureAwait(false);
            }
            else if (matchingProfile == null && _currentProfile is not null)
            {
                _logger?.LogInformation("ゲームが終了、デフォルトプロファイルに戻します");
                
                // デフォルトプロファイルに戻す（軽量な設定取得・適用処理のため同期実行）
                #pragma warning disable CA1849 // Call async methods when in an async method
                var defaultProfile = _profileManager.GetDefaultProfile();
                _captureService.ApplyGameProfile(defaultProfile);
                #pragma warning restore CA1849
                _currentProfile = null;
                
                // イベント発行
                await PublishGameExitedEventAsync(processName).ConfigureAwait(false);
            }
            
            _lastDetectedGame = gameId;
        }
        catch (InvalidOperationException ex)
        {
            _logger?.LogError(ex, "ゲーム検出・プロファイル適用中にエラーが発生");
        }
        catch (TimeoutException ex)
        {
            _logger?.LogError(ex, "ゲーム検出・プロファイル適用中にタイムアウトが発生");
        }
    }
    
    private static Process? GetActiveProcess()
    {
        try
        {
            // フォアグラウンドウィンドウのプロセスを取得
            var foregroundWindow = GetForegroundWindow();
            if (foregroundWindow == IntPtr.Zero)
                return null;
            
            var result = GetWindowThreadProcessId(foregroundWindow, out uint processId);
            if (result == 0 || processId == 0)
                return null;
                
            return Process.GetProcessById((int)processId);
        }
        catch (ArgumentException)
        {
            // プロセスIDが無効またはプロセスが終了済み
            return null;
        }
        catch (InvalidOperationException)
        {
            // プロセスアクセスエラー
            return null;
        }
    }
    
    private async Task PublishGameDetectedEventAsync(string processName, string? windowTitle, SettingsGameCaptureProfile profile)
    {
        var gameDetectedEvent = new GameDetectedEvent(processName, windowTitle, profile);
        await _eventAggregator.PublishAsync(gameDetectedEvent).ConfigureAwait(false);
    }
    
    private async Task PublishGameExitedEventAsync(string processName)
    {
        var gameExitedEvent = new GameExitedEvent(processName);
        await _eventAggregator.PublishAsync(gameExitedEvent).ConfigureAwait(false);
    }
    
    #region Win32 API
    
    [System.Runtime.InteropServices.LibraryImport("user32.dll", SetLastError = true)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5392:Use DefaultDllImportSearchPaths attribute for P/Invokes", 
        Justification = "Standard Windows API calls with minimal security risk")]
    private static partial IntPtr GetForegroundWindow();
    
    [System.Runtime.InteropServices.LibraryImport("user32.dll", SetLastError = true)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5392:Use DefaultDllImportSearchPaths attribute for P/Invokes", 
        Justification = "Standard Windows API calls with minimal security risk")]
    private static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    
    #endregion
    
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
    
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            // 非同期メソッドを呼ばずに直接処理を実行
            lock (_syncLock)
            {
                if (IsDetectionRunning)
                {
                    // Timer.Dispose()には非同期バージョンが存在しないため同期実行
                    #pragma warning disable CA1849 // Call async methods when in an async method
                    _detectionTimer?.Dispose();
                    #pragma warning restore CA1849
                    _detectionTimer = null;
                    IsDetectionRunning = false;
                    
                    _logger?.LogInformation("ゲーム自動検出を停止しました（Dispose）");
                }
            }
        }
    }
}

/// <summary>
/// ゲーム検出イベント
/// </summary>
public class GameDetectedEvent : EventBase
{
    /// <summary>
    /// 検出されたゲームのプロセス名
    /// </summary>
    public string ProcessName { get; }
    
    /// <summary>
    /// 検出されたゲームのウィンドウタイトル
    /// </summary>
    public string? WindowTitle { get; }
    
    /// <summary>
    /// 適用されたプロファイル
    /// </summary>
    public SettingsGameCaptureProfile AppliedProfile { get; }
    
    /// <summary>
    /// 検出された時刻
    /// </summary>
    public DateTime DetectedAt { get; }
    
    public GameDetectedEvent(string processName, string? windowTitle, SettingsGameCaptureProfile appliedProfile)
    {
        ProcessName = processName ?? throw new ArgumentNullException(nameof(processName));
        WindowTitle = windowTitle;
        AppliedProfile = appliedProfile ?? throw new ArgumentNullException(nameof(appliedProfile));
        DetectedAt = DateTime.Now;
    }
    
    public override string Name => "GameDetected";
    public override string Category => "GameDetection";
}

/// <summary>
/// ゲーム終了イベント
/// </summary>
public class GameExitedEvent : EventBase
{
    /// <summary>
    /// 終了したゲームのプロセス名
    /// </summary>
    public string ProcessName { get; }
    
    /// <summary>
    /// 終了が検出された時刻
    /// </summary>
    public DateTime ExitedAt { get; }
    
    public GameExitedEvent(string processName)
    {
        ProcessName = processName ?? throw new ArgumentNullException(nameof(processName));
        ExitedAt = DateTime.Now;
    }
    
    public override string Name => "GameExited";
    public override string Category => "GameDetection";
}
