# 実装: キャプチャ最適化とサービス統合

## 概要
キャプチャ機能の最適化と、それを統合したキャプチャサービスの実装を行います。

## 目的・理由
キャプチャは頻繁に実行される処理であり、システム全体のパフォーマンスに大きな影響を与えます。キャプチャ間隔の最適化、リソース使用量の削減、およびイベント通知などの機能を統合したキャプチャサービスを実装することで、効率的なOCR処理の基盤を構築します。

## 詳細
- キャプチャ最適化オプションの実装
- ゲームごとのキャプチャプロファイル管理
- キャプチャサービスの実装
- キャプチャイベント通知の実装

## タスク分解
- [ ] キャプチャ最適化機能の実装
  - [ ] キャプチャ間隔の動的調整
  - [ ] キャプチャ領域の動的調整
  - [ ] リソース使用量の監視と制御
- [ ] `ICaptureService`インターフェースの実装
  - [ ] 連続キャプチャ機能の実装
  - [ ] キャプチャステータス管理
  - [ ] キャプチャコントロール（開始・停止・一時停止）
- [ ] キャプチャイベント通知の実装
  - [ ] `CaptureCompletedEvent`の実装
  - [ ] `CaptureFailedEvent`の実装
  - [ ] イベント集約機構との連携
- [ ] キャプチャプロファイル管理
  - [ ] プロファイル設定クラスの実装
  - [ ] プロファイル永続化機能
  - [ ] ゲーム検出との連携

## キャプチャサービス設計案
```csharp
namespace Baketa.Core.Abstractions.Capture
{
    /// <summary>
    /// キャプチャサービスのステータス
    /// </summary>
    public enum CaptureServiceStatus
    {
        /// <summary>
        /// 停止状態
        /// </summary>
        Stopped,
        
        /// <summary>
        /// キャプチャ実行中
        /// </summary>
        Running,
        
        /// <summary>
        /// 一時停止中
        /// </summary>
        Paused,
        
        /// <summary>
        /// エラー状態
        /// </summary>
        Error
    }
    
    /// <summary>
    /// キャプチャサービスのインターフェース
    /// </summary>
    public interface ICaptureService
    {
        /// <summary>
        /// 現在のキャプチャサービスのステータス
        /// </summary>
        CaptureServiceStatus Status { get; }
        
        /// <summary>
        /// 最後にキャプチャした画像
        /// </summary>
        IImage? LastCapturedImage { get; }
        
        /// <summary>
        /// 最後にキャプチャした時刻
        /// </summary>
        DateTime? LastCaptureTime { get; }
        
        /// <summary>
        /// 連続キャプチャを開始します
        /// </summary>
        /// <param name="interval">キャプチャ間隔（ミリ秒）</param>
        /// <param name="cancellationToken">キャンセルトークン</param>
        Task StartContinuousCaptureAsync(int interval = 1000, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// 連続キャプチャを停止します
        /// </summary>
        Task StopCaptureAsync();
        
        /// <summary>
        /// 連続キャプチャを一時停止します
        /// </summary>
        Task PauseCaptureAsync();
        
        /// <summary>
        /// 連続キャプチャを再開します
        /// </summary>
        Task ResumeCaptureAsync();
        
        /// <summary>
        /// 1回のキャプチャを実行します
        /// </summary>
        /// <returns>キャプチャした画像</returns>
        Task<IImage?> CaptureOnceAsync();
        
        /// <summary>
        /// キャプチャ設定を適用します
        /// </summary>
        /// <param name="settings">キャプチャ設定</param>
        void ApplySettings(CaptureSettings settings);
        
        /// <summary>
        /// 現在のキャプチャ設定を取得します
        /// </summary>
        /// <returns>キャプチャ設定</returns>
        CaptureSettings GetSettings();
    }
    
    /// <summary>
    /// キャプチャ設定を表すクラス
    /// </summary>
    public class CaptureSettings
    {
        /// <summary>
        /// キャプチャ対象のウィンドウタイトル（空文字列の場合はアクティブウィンドウ）
        /// </summary>
        public string TargetWindowTitle { get; set; } = string.Empty;
        
        /// <summary>
        /// キャプチャ領域（nullの場合はウィンドウ全体）
        /// </summary>
        public Rectangle? CaptureRegion { get; set; }
        
        /// <summary>
        /// キャプチャ間隔（ミリ秒）
        /// </summary>
        public int CaptureInterval { get; set; } = 1000;
        
        /// <summary>
        /// 差分検出を使用するか
        /// </summary>
        public bool UseDifferenceDetection { get; set; } = true;
        
        /// <summary>
        /// キャプチャ解像度（元の解像度から縮小する場合）
        /// </summary>
        public Size? CaptureResolution { get; set; }
        
        /// <summary>
        /// 自動間隔調整を使用するか
        /// </summary>
        public bool UseAdaptiveInterval { get; set; } = false;
        
        /// <summary>
        /// 最小キャプチャ間隔（自動調整時、ミリ秒）
        /// </summary>
        public int MinCaptureInterval { get; set; } = 100;
        
        /// <summary>
        /// 最大キャプチャ間隔（自動調整時、ミリ秒）
        /// </summary>
        public int MaxCaptureInterval { get; set; } = 5000;
        
        /// <summary>
        /// システム負荷が高い場合に自動的にキャプチャ間隔を調整するか
        /// </summary>
        public bool AdjustIntervalOnHighLoad { get; set; } = true;
        
        /// <summary>
        /// 高負荷とみなすCPU使用率（%）
        /// </summary>
        public int HighLoadCpuThreshold { get; set; } = 80;
    }
}
```

## キャプチャサービス実装例
```csharp
namespace Baketa.Application.Services.Capture
{
    /// <summary>
    /// キャプチャサービスの実装
    /// </summary>
    public class CaptureService : ICaptureService, IDisposable
    {
        private readonly IGdiScreenCapturer _capturer;
        private readonly IWindowManager _windowManager;
        private readonly IDifferenceDetector _differenceDetector;
        private readonly IEventAggregator _eventAggregator;
        private readonly ILogger<CaptureService>? _logger;
        
        private CaptureSettings _settings = new();
        private CaptureServiceStatus _status = CaptureServiceStatus.Stopped;
        private IImage? _lastCapturedImage;
        private DateTime? _lastCaptureTime;
        
        private Task? _captureTask;
        private CancellationTokenSource? _cancellationTokenSource;
        private readonly object _syncLock = new();
        
        // パフォーマンス監視用
        private readonly PerformanceCounter _cpuCounter;
        private readonly Stopwatch _captureStopwatch = new();
        private readonly Queue<long> _captureTimes = new(10); // 直近10回の計測値
        
        public CaptureServiceStatus Status => _status;
        public IImage? LastCapturedImage => _lastCapturedImage;
        public DateTime? LastCaptureTime => _lastCaptureTime;
        
        public CaptureService(
            IGdiScreenCapturer capturer,
            IWindowManager windowManager,
            IDifferenceDetector differenceDetector,
            IEventAggregator eventAggregator,
            ILogger<CaptureService>? logger = null)
        {
            _capturer = capturer ?? throw new ArgumentNullException(nameof(capturer));
            _windowManager = windowManager ?? throw new ArgumentNullException(nameof(windowManager));
            _differenceDetector = differenceDetector ?? throw new ArgumentNullException(nameof(differenceDetector));
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
            _logger = logger;
            
            // CPU使用率監視の初期化
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
        }
        
        public async Task StartContinuousCaptureAsync(int interval = 1000, CancellationToken cancellationToken = default)
        {
            lock (_syncLock)
            {
                if (_status == CaptureServiceStatus.Running)
                    return;
                    
                _settings.CaptureInterval = interval;
                _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _status = CaptureServiceStatus.Running;
                
                _logger?.LogInformation("連続キャプチャを開始: 間隔={Interval}ms", interval);
            }
            
            _captureTask = ContinuousCaptureAsync(_cancellationTokenSource.Token);
            
            try
            {
                // 最初のキャプチャを即時実行
                await CaptureOnceAsync();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "最初のキャプチャに失敗");
                _status = CaptureServiceStatus.Error;
                throw;
            }
        }
        
        public Task StopCaptureAsync()
        {
            lock (_syncLock)
            {
                if (_status == CaptureServiceStatus.Stopped)
                    return Task.CompletedTask;
                    
                _cancellationTokenSource?.Cancel();
                _status = CaptureServiceStatus.Stopped;
                _logger?.LogInformation("キャプチャを停止");
            }
            
            return Task.CompletedTask;
        }
        
        public Task PauseCaptureAsync()
        {
            lock (_syncLock)
            {
                if (_status != CaptureServiceStatus.Running)
                    return Task.CompletedTask;
                    
                _status = CaptureServiceStatus.Paused;
                _logger?.LogInformation("キャプチャを一時停止");
            }
            
            return Task.CompletedTask;
        }
        
        public Task ResumeCaptureAsync()
        {
            lock (_syncLock)
            {
                if (_status != CaptureServiceStatus.Paused)
                    return Task.CompletedTask;
                    
                _status = CaptureServiceStatus.Running;
                _logger?.LogInformation("キャプチャを再開");
            }
            
            return Task.CompletedTask;
        }
        
        public async Task<IImage?> CaptureOnceAsync()
        {
            IImage? capturedImage = null;
            bool hasChange = true;
            
            try
            {
                _captureStopwatch.Restart();
                
                // ターゲットウィンドウの取得
                IntPtr targetWindow = IntPtr.Zero;
                if (!string.IsNullOrEmpty(_settings.TargetWindowTitle))
                {
                    targetWindow = _windowManager.FindWindowByTitle(_settings.TargetWindowTitle);
                    if (targetWindow == IntPtr.Zero)
                    {
                        _logger?.LogWarning("指定されたタイトルのウィンドウが見つかりません: {Title}", _settings.TargetWindowTitle);
                        targetWindow = _windowManager.GetForegroundWindow();
                    }
                }
                else
                {
                    targetWindow = _windowManager.GetForegroundWindow();
                }
                
                // キャプチャ実行
                IWindowsImage windowsImage;
                if (_settings.CaptureRegion.HasValue)
                {
                    windowsImage = await _capturer.CaptureRegionAsync(_settings.CaptureRegion.Value);
                }
                else if (targetWindow != IntPtr.Zero)
                {
                    windowsImage = await _capturer.CaptureWindowAsync(targetWindow);
                }
                else
                {
                    windowsImage = await _capturer.CaptureScreenAsync();
                }
                
                // キャプチャした画像の解像度調整（必要な場合）
                if (_settings.CaptureResolution.HasValue &&
                    (windowsImage.Width != _settings.CaptureResolution.Value.Width ||
                     windowsImage.Height != _settings.CaptureResolution.Value.Height))
                {
                    var resized = await windowsImage.ResizeAsync(
                        _settings.CaptureResolution.Value.Width, 
                        _settings.CaptureResolution.Value.Height);
                    
                    windowsImage.Dispose();
                    windowsImage = (IWindowsImage)resized;
                }
                
                // IImage型に変換
                capturedImage = new ImageAdapter(windowsImage);
                
                // 差分検出
                if (_settings.UseDifferenceDetection && _lastCapturedImage != null)
                {
                    hasChange = await _differenceDetector.HasSignificantChangeAsync(_lastCapturedImage, capturedImage);
                }
                
                // 画像更新と時刻記録
                _lastCapturedImage?.Dispose();
                _lastCapturedImage = capturedImage;
                _lastCaptureTime = DateTime.Now;
                
                _captureStopwatch.Stop();
                
                // キャプチャ時間の記録
                lock (_captureTimes)
                {
                    _captureTimes.Enqueue(_captureStopwatch.ElapsedMilliseconds);
                    if (_captureTimes.Count > 10)
                        _captureTimes.Dequeue();
                }
                
                _logger?.LogDebug("キャプチャ完了: {Width}x{Height}, 変化検出={HasChange}, 処理時間={ElapsedMs}ms",
                    capturedImage.Width, capturedImage.Height, hasChange, _captureStopwatch.ElapsedMilliseconds);
                
                // 変化がある場合のみイベント発行
                if (hasChange)
                {
                    await _eventAggregator.PublishAsync(new CaptureCompletedEvent(
                        capturedImage,
                        _settings.CaptureRegion ?? new Rectangle(0, 0, capturedImage.Width, capturedImage.Height),
                        _captureStopwatch.Elapsed));
                }
                
                // 適応的間隔調整
                AdjustCaptureIntervalIfNeeded();
                
                return capturedImage;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "キャプチャ処理中にエラーが発生");
                _status = CaptureServiceStatus.Error;
                
                await _eventAggregator.PublishAsync(new CaptureFailedEvent(ex.Message, ex));
                
                capturedImage?.Dispose();
                throw;
            }
        }
        
        private async Task ContinuousCaptureAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (_status == CaptureServiceStatus.Running)
                    {
                        try
                        {
                            await CaptureOnceAsync();
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogError(ex, "連続キャプチャ処理中にエラーが発生");
                            // エラーが発生しても継続
                        }
                    }
                    
                    // 次のキャプチャまで待機
                    await Task.Delay(_settings.CaptureInterval, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // キャンセルは正常終了
                _logger?.LogInformation("連続キャプチャがキャンセルされました");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "連続キャプチャタスクでエラーが発生");
                _status = CaptureServiceStatus.Error;
            }
            finally
            {
                _status = CaptureServiceStatus.Stopped;
            }
        }
        
        private void AdjustCaptureIntervalIfNeeded()
        {
            if (!_settings.UseAdaptiveInterval)
                return;
                
            // CPU使用率の取得
            float cpuUsage = _cpuCounter.NextValue();
            
            // 高負荷時の調整
            if (_settings.AdjustIntervalOnHighLoad && cpuUsage > _settings.HighLoadCpuThreshold)
            {
                // 間隔を徐々に増加
                _settings.CaptureInterval = Math.Min(
                    _settings.CaptureInterval + 100,
                    _settings.MaxCaptureInterval);
                    
                _logger?.LogDebug("高CPU使用率 ({CpuUsage:F1}%) を検出: キャプチャ間隔を {Interval}ms に調整",
                    cpuUsage, _settings.CaptureInterval);
                return;
            }
            
            // 平均キャプチャ時間の計算
            double avgCaptureTime = 0;
            if (_captureTimes.Count > 0)
            {
                lock (_captureTimes)
                {
                    avgCaptureTime = _captureTimes.Average();
                }
            }
            
            // キャプチャ時間に基づく調整
            if (avgCaptureTime > 0)
            {
                // 理想的には、キャプチャ時間の3倍程度の間隔が良い
                int idealInterval = (int)(avgCaptureTime * 3);
                idealInterval = Math.Max(idealInterval, _settings.MinCaptureInterval);
                idealInterval = Math.Min(idealInterval, _settings.MaxCaptureInterval);
                
                // 現在の間隔と大きく違う場合のみ調整
                if (Math.Abs(_settings.CaptureInterval - idealInterval) > 100)
                {
                    _settings.CaptureInterval = idealInterval;
                    _logger?.LogDebug("キャプチャ時間に基づき間隔を {Interval}ms に調整 (平均キャプチャ時間: {AvgTime:F1}ms)",
                        _settings.CaptureInterval, avgCaptureTime);
                }
            }
        }
        
        public void ApplySettings(CaptureSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
                
            lock (_syncLock)
            {
                _settings = new CaptureSettings
                {
                    TargetWindowTitle = settings.TargetWindowTitle,
                    CaptureRegion = settings.CaptureRegion,
                    CaptureInterval = settings.CaptureInterval,
                    UseDifferenceDetection = settings.UseDifferenceDetection,
                    CaptureResolution = settings.CaptureResolution,
                    UseAdaptiveInterval = settings.UseAdaptiveInterval,
                    MinCaptureInterval = settings.MinCaptureInterval,
                    MaxCaptureInterval = settings.MaxCaptureInterval,
                    AdjustIntervalOnHighLoad = settings.AdjustIntervalOnHighLoad,
                    HighLoadCpuThreshold = settings.HighLoadCpuThreshold
                };
                
                _logger?.LogInformation("キャプチャ設定を更新: 間隔={Interval}ms, 差分検出={UseDiff}, 適応的間隔={UseAdaptive}",
                    _settings.CaptureInterval, _settings.UseDifferenceDetection, _settings.UseAdaptiveInterval);
            }
        }
        
        public CaptureSettings GetSettings()
        {
            lock (_syncLock)
            {
                return new CaptureSettings
                {
                    TargetWindowTitle = _settings.TargetWindowTitle,
                    CaptureRegion = _settings.CaptureRegion,
                    CaptureInterval = _settings.CaptureInterval,
                    UseDifferenceDetection = _settings.UseDifferenceDetection,
                    CaptureResolution = _settings.CaptureResolution,
                    UseAdaptiveInterval = _settings.UseAdaptiveInterval,
                    MinCaptureInterval = _settings.MinCaptureInterval,
                    MaxCaptureInterval = _settings.MaxCaptureInterval,
                    AdjustIntervalOnHighLoad = _settings.AdjustIntervalOnHighLoad,
                    HighLoadCpuThreshold = _settings.HighLoadCpuThreshold
                };
            }
        }
        
        public void Dispose()
        {
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cpuCounter?.Dispose();
            _lastCapturedImage?.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// キャプチャ完了イベント
    /// </summary>
    public class CaptureCompletedEvent : EventBase
    {
        /// <summary>
        /// キャプチャした画像
        /// </summary>
        public IImage CapturedImage { get; }
        
        /// <summary>
        /// キャプチャした領域
        /// </summary>
        public Rectangle CaptureRegion { get; }
        
        /// <summary>
        /// キャプチャ処理時間
        /// </summary>
        public TimeSpan CaptureTime { get; }
        
        public CaptureCompletedEvent(IImage capturedImage, Rectangle captureRegion, TimeSpan captureTime)
        {
            CapturedImage = capturedImage ?? throw new ArgumentNullException(nameof(capturedImage));
            CaptureRegion = captureRegion;
            CaptureTime = captureTime;
        }
    }
    
    /// <summary>
    /// キャプチャ失敗イベント
    /// </summary>
    public class CaptureFailedEvent : EventBase
    {
        /// <summary>
        /// エラーメッセージ
        /// </summary>
        public string ErrorMessage { get; }
        
        /// <summary>
        /// 例外
        /// </summary>
        public Exception? Exception { get; }
        
        public CaptureFailedEvent(string errorMessage, Exception? exception = null)
        {
            ErrorMessage = errorMessage ?? throw new ArgumentNullException(nameof(errorMessage));
            Exception = exception;
        }
    }
}
```

## 関連Issue/参考
- 親Issue: #6 実装: キャプチャサブシステムの実装
- 依存: #6.1 実装: GDIベースのキャプチャメソッド
- 依存: #6.2 実装: 差分検出サブシステム
- 関連: #4 実装: イベント集約機構の構築
- 参照: E:\dev\Baketa\docs\3-architecture\improved-architecture.md (6.2 アプリケーションサービス実装)
- 参照: E:\dev\Baketa\docs\2-development\coding-standards\csharp-standards.md (6.3 パフォーマンス測定)

## マイルストーン
マイルストーン2: キャプチャとOCR基盤

## ラベル
- `type: feature`
- `priority: high`
- `component: core`
