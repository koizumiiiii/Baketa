# Issue 11-4: マルチモニターサポートの実装

## 概要
複数のモニターを使用する環境でオーバーレイウィンドウを適切に表示・管理する機能を実装します。これにより、マルチモニター環境でもゲームと翻訳オーバーレイの連携が正しく機能するようになります。

## 目的・理由
マルチモニターサポートは以下の理由で重要です：

1. ユーザーが複数のモニターを使用する場合でも一貫した体験を提供する
2. ゲームがプライマリモニターではないディスプレイで実行されている場合にも正しく動作させる
3. 複数のモニターにまたがって表示されるゲームや、モニター間を移動するゲームウィンドウにも対応する
4. モニターごとの異なるDPI設定やスケーリングに適切に対応する

## 詳細
- モニター検出と管理システムの実装
- モニター間のウィンドウ移動追跡機能の実装
- DPIスケーリング対応の実装
- マルチモニター環境での位置計算最適化

## タスク分解
- [ ] モニター情報管理
  - [ ] `IMonitorManager`インターフェースの設計
  - [ ] `MonitorManager`クラスの実装
  - [ ] モニター列挙と情報収集機能の実装
  - [ ] モニター変更イベント検出機能の実装
- [ ] DPI管理
  - [ ] モニターごとのDPI情報取得機能
  - [ ] DPI変更の検出と対応機能
  - [ ] 座標変換ユーティリティの実装
- [ ] ウィンドウ追跡
  - [ ] ゲームウィンドウのモニター間移動検出
  - [ ] ウィンドウ境界情報の取得と管理
  - [ ] モニター間座標変換の実装
- [ ] オーバーレイ位置管理
  - [ ] マルチモニター対応の座標計算
  - [ ] モニター境界をまたぐ場合の処理
  - [ ] モニター間移動時の位置保持機能
- [ ] イベント処理
  - [ ] モニター接続・切断イベント処理
  - [ ] DPI変更イベント処理
  - [ ] モニター設定変更イベント処理
- [ ] 設定対応
  - [ ] モニター固有設定の管理
  - [ ] プライマリモニター優先設定の実装
  - [ ] ユーザー指定モニター設定の実装
- [ ] 単体テストの実装

## インターフェース設計案
```csharp
namespace Baketa.UI.Monitors
{
    /// <summary>
    /// モニターマネージャーインターフェース
    /// </summary>
    public interface IMonitorManager
    {
        /// <summary>
        /// 利用可能なモニターのコレクション
        /// </summary>
        IReadOnlyList<MonitorInfo> Monitors { get; }
        
        /// <summary>
        /// プライマリモニター
        /// </summary>
        MonitorInfo PrimaryMonitor { get; }
        
        /// <summary>
        /// モニター設定変更イベント
        /// </summary>
        event EventHandler<MonitorChangedEventArgs> MonitorChanged;
        
        /// <summary>
        /// DPI変更イベント
        /// </summary>
        event EventHandler<DpiChangedEventArgs> DpiChanged;
        
        /// <summary>
        /// ウィンドウが表示されているモニターを取得します
        /// </summary>
        /// <param name="windowHandle">ウィンドウハンドル</param>
        /// <returns>モニター情報</returns>
        MonitorInfo GetMonitorFromWindow(IntPtr windowHandle);
        
        /// <summary>
        /// 座標が含まれるモニターを取得します
        /// </summary>
        /// <param name="point">スクリーン座標</param>
        /// <returns>モニター情報</returns>
        MonitorInfo GetMonitorFromPoint(Point point);
        
        /// <summary>
        /// モニター間で座標を変換します
        /// </summary>
        /// <param name="point">変換する座標</param>
        /// <param name="sourceMonitor">元のモニター</param>
        /// <param name="targetMonitor">対象のモニター</param>
        /// <returns>変換後の座標</returns>
        Point TransformPointBetweenMonitors(Point point, MonitorInfo sourceMonitor, MonitorInfo targetMonitor);
        
        /// <summary>
        /// モニター情報を更新します
        /// </summary>
        void RefreshMonitors();
        
        /// <summary>
        /// モニター監視を開始します
        /// </summary>
        void StartMonitoring();
        
        /// <summary>
        /// モニター監視を停止します
        /// </summary>
        void StopMonitoring();
    }
    
    /// <summary>
    /// モニター情報クラス
    /// </summary>
    public class MonitorInfo
    {
        /// <summary>
        /// モニターハンドル
        /// </summary>
        public IntPtr Handle { get; }
        
        /// <summary>
        /// モニターの名前
        /// </summary>
        public string Name { get; }
        
        /// <summary>
        /// モニターの一意識別子
        /// </summary>
        public string DeviceId { get; }
        
        /// <summary>
        /// モニターのスクリーン領域
        /// </summary>
        public Rect Bounds { get; }
        
        /// <summary>
        /// モニターの作業領域（タスクバーなどを除いた領域）
        /// </summary>
        public Rect WorkArea { get; }
        
        /// <summary>
        /// プライマリモニターかどうか
        /// </summary>
        public bool IsPrimary { get; }
        
        /// <summary>
        /// 水平DPI
        /// </summary>
        public double DpiX { get; }
        
        /// <summary>
        /// 垂直DPI
        /// </summary>
        public double DpiY { get; }
        
        /// <summary>
        /// スケールファクター
        /// </summary>
        public double ScaleFactor { get; }
        
        /// <summary>
        /// 新しいモニター情報を初期化します
        /// </summary>
        /// <param name="handle">モニターハンドル</param>
        /// <param name="name">モニター名</param>
        /// <param name="deviceId">デバイスID</param>
        /// <param name="bounds">スクリーン領域</param>
        /// <param name="workArea">作業領域</param>
        /// <param name="isPrimary">プライマリモニターかどうか</param>
        /// <param name="dpiX">水平DPI</param>
        /// <param name="dpiY">垂直DPI</param>
        public MonitorInfo(
            IntPtr handle, 
            string name, 
            string deviceId, 
            Rect bounds, 
            Rect workArea, 
            bool isPrimary, 
            double dpiX, 
            double dpiY)
        {
            Handle = handle;
            Name = name;
            DeviceId = deviceId;
            Bounds = bounds;
            WorkArea = workArea;
            IsPrimary = isPrimary;
            DpiX = dpiX;
            DpiY = dpiY;
            ScaleFactor = dpiX / 96.0; // 標準DPI (96) に対するスケールファクター
        }
        
        /// <summary>
        /// 指定した座標がこのモニターに含まれるかどうかを判定します
        /// </summary>
        /// <param name="point">判定する座標</param>
        /// <returns>含まれていればtrue</returns>
        public bool ContainsPoint(Point point)
        {
            return Bounds.Contains(point);
        }
        
        /// <summary>
        /// 指定した矩形がこのモニターに含まれるかどうかを判定します
        /// </summary>
        /// <param name="rect">判定する矩形</param>
        /// <param name="entirelyContained">完全に含まれる必要があるかどうか</param>
        /// <returns>含まれていればtrue</returns>
        public bool ContainsRect(Rect rect, bool entirelyContained = false)
        {
            if (entirelyContained)
            {
                return Bounds.Contains(rect);
            }
            else
            {
                return Bounds.Intersects(rect);
            }
        }
        
        /// <summary>
        /// 物理的なピクセル座標をDPI非依存の論理座標に変換します
        /// </summary>
        /// <param name="physicalPoint">物理的なピクセル座標</param>
        /// <returns>論理座標</returns>
        public Point PhysicalToLogical(Point physicalPoint)
        {
            return new Point(
                physicalPoint.X / ScaleFactor,
                physicalPoint.Y / ScaleFactor);
        }
        
        /// <summary>
        /// DPI非依存の論理座標を物理的なピクセル座標に変換します
        /// </summary>
        /// <param name="logicalPoint">論理座標</param>
        /// <returns>物理的なピクセル座標</returns>
        public Point LogicalToPhysical(Point logicalPoint)
        {
            return new Point(
                logicalPoint.X * ScaleFactor,
                logicalPoint.Y * ScaleFactor);
        }
        
        /// <summary>
        /// 表示名を取得します
        /// </summary>
        /// <returns>表示名</returns>
        public override string ToString()
        {
            return $"{Name} ({Bounds.Width}x{Bounds.Height}{(IsPrimary ? ", Primary" : "")})";
        }
    }
    
    /// <summary>
    /// モニター変更イベント引数
    /// </summary>
    public class MonitorChangedEventArgs : EventArgs
    {
        /// <summary>
        /// 変更タイプ
        /// </summary>
        public MonitorChangeType ChangeType { get; }
        
        /// <summary>
        /// 追加/削除/変更されたモニター情報
        /// </summary>
        public MonitorInfo? AffectedMonitor { get; }
        
        /// <summary>
        /// 全モニターリスト
        /// </summary>
        public IReadOnlyList<MonitorInfo> Monitors { get; }
        
        /// <summary>
        /// 新しいモニター変更イベント引数を初期化します
        /// </summary>
        /// <param name="changeType">変更タイプ</param>
        /// <param name="affectedMonitor">影響を受けたモニター</param>
        /// <param name="monitors">全モニターリスト</param>
        public MonitorChangedEventArgs(
            MonitorChangeType changeType,
            MonitorInfo? affectedMonitor,
            IReadOnlyList<MonitorInfo> monitors)
        {
            ChangeType = changeType;
            AffectedMonitor = affectedMonitor;
            Monitors = monitors;
        }
    }
    
    /// <summary>
    /// モニター変更タイプ
    /// </summary>
    public enum MonitorChangeType
    {
        /// <summary>
        /// モニターが追加された
        /// </summary>
        Added,
        
        /// <summary>
        /// モニターが削除された
        /// </summary>
        Removed,
        
        /// <summary>
        /// モニター設定が変更された
        /// </summary>
        Changed,
        
        /// <summary>
        /// プライマリモニターが変更された
        /// </summary>
        PrimaryChanged,
        
        /// <summary>
        /// すべてのモニターが更新された
        /// </summary>
        RefreshAll
    }
    
    /// <summary>
    /// DPI変更イベント引数
    /// </summary>
    public class DpiChangedEventArgs : EventArgs
    {
        /// <summary>
        /// 影響を受けたモニター
        /// </summary>
        public MonitorInfo Monitor { get; }
        
        /// <summary>
        /// 以前のDPI X
        /// </summary>
        public double OldDpiX { get; }
        
        /// <summary>
        /// 以前のDPI Y
        /// </summary>
        public double OldDpiY { get; }
        
        /// <summary>
        /// 新しいDPI X
        /// </summary>
        public double NewDpiX { get; }
        
        /// <summary>
        /// 新しいDPI Y
        /// </summary>
        public double NewDpiY { get; }
        
        /// <summary>
        /// 新しいDPI変更イベント引数を初期化します
        /// </summary>
        /// <param name="monitor">モニター情報</param>
        /// <param name="oldDpiX">以前のDPI X</param>
        /// <param name="oldDpiY">以前のDPI Y</param>
        /// <param name="newDpiX">新しいDPI X</param>
        /// <param name="newDpiY">新しいDPI Y</param>
        public DpiChangedEventArgs(
            MonitorInfo monitor,
            double oldDpiX,
            double oldDpiY,
            double newDpiX,
            double newDpiY)
        {
            Monitor = monitor;
            OldDpiX = oldDpiX;
            OldDpiY = oldDpiY;
            NewDpiX = newDpiX;
            NewDpiY = newDpiY;
        }
    }
}
```

## モニターマネージャー実装案
```csharp
namespace Baketa.UI.Monitors
{
    /// <summary>
    /// Windowsプラットフォーム用モニターマネージャー実装
    /// </summary>
    public class WindowsMonitorManager : IMonitorManager, IDisposable
    {
        // Win32 API定義
        // 省略
        
        private readonly ILogger? _logger;
        private readonly List<MonitorInfo> _monitors = new();
        private IntPtr _messageWindowHandle;
        private bool _isMonitoring;
        private bool _disposed;
        
        /// <summary>
        /// 新しいWindowsモニターマネージャーを初期化します
        /// </summary>
        /// <param name="logger">ロガー</param>
        public WindowsMonitorManager(ILogger? logger = null)
        {
            _logger = logger;
            
            // モニター情報を収集
            RefreshMonitors();
            
            _logger?.LogInformation("Windowsモニターマネージャーが初期化されました。検出モニター数: {Count}", _monitors.Count);
        }
        
        /// <inheritdoc />
        public IReadOnlyList<MonitorInfo> Monitors => _monitors;
        
        /// <inheritdoc />
        public MonitorInfo PrimaryMonitor => _monitors.FirstOrDefault(m => m.IsPrimary) ?? _monitors.FirstOrDefault() ?? throw new InvalidOperationException("モニターが見つかりません。");
        
        /// <inheritdoc />
        public event EventHandler<MonitorChangedEventArgs>? MonitorChanged;
        
        /// <inheritdoc />
        public event EventHandler<DpiChangedEventArgs>? DpiChanged;
        
        /// <inheritdoc />
        public MonitorInfo GetMonitorFromWindow(IntPtr windowHandle)
        {
            if (windowHandle == IntPtr.Zero)
                throw new ArgumentException("ウィンドウハンドルが無効です。", nameof(windowHandle));
                
            var monitorHandle = MonitorFromWindow(windowHandle, MONITOR_DEFAULTTONEAREST);
            return _monitors.FirstOrDefault(m => m.Handle == monitorHandle) ?? PrimaryMonitor;
        }
        
        /// <inheritdoc />
        public MonitorInfo GetMonitorFromPoint(Point point)
        {
            var pt = new POINT { X = (int)point.X, Y = (int)point.Y };
            var monitorHandle = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
            return _monitors.FirstOrDefault(m => m.Handle == monitorHandle) ?? PrimaryMonitor;
        }
        
        /// <inheritdoc />
        public Point TransformPointBetweenMonitors(Point point, MonitorInfo sourceMonitor, MonitorInfo targetMonitor)
        {
            if (sourceMonitor == null)
                throw new ArgumentNullException(nameof(sourceMonitor));
                
            if (targetMonitor == null)
                throw new ArgumentNullException(nameof(targetMonitor));
                
            // 物理的なピクセル座標への変換
            var physicalPoint = sourceMonitor.LogicalToPhysical(point);
            
            // モニターの原点を考慮し、グローバル座標を計算
            var globalX = physicalPoint.X + sourceMonitor.Bounds.X;
            var globalY = physicalPoint.Y + sourceMonitor.Bounds.Y;
            
            // ターゲットモニターのローカル座標に変換
            var targetPhysicalX = globalX - targetMonitor.Bounds.X;
            var targetPhysicalY = globalY - targetMonitor.Bounds.Y;
            
            // 論理座標に戻す
            var targetLogicalPoint = targetMonitor.PhysicalToLogical(new Point(targetPhysicalX, targetPhysicalY));
            
            return targetLogicalPoint;
        }
        
        /// <inheritdoc />
        public void RefreshMonitors()
        {
            _monitors.Clear();
            
            // モニター列挙コールバック関数
            bool EnumMonitorsCallback(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData)
            {
                try
                {
                    // モニター情報を取得
                    var monitorInfo = new MONITORINFOEX();
                    monitorInfo.cbSize = Marshal.SizeOf<MONITORINFOEX>();
                    
                    if (GetMonitorInfo(hMonitor, ref monitorInfo))
                    {
                        var deviceName = new string(monitorInfo.szDevice).TrimEnd('\0');
                        var isPrimary = (monitorInfo.dwFlags & MONITORINFOF_PRIMARY) != 0;
                        
                        // スクリーン座標と作業領域
                        var bounds = new Rect(
                            monitorInfo.rcMonitor.Left,
                            monitorInfo.rcMonitor.Top,
                            monitorInfo.rcMonitor.Right - monitorInfo.rcMonitor.Left,
                            monitorInfo.rcMonitor.Bottom - monitorInfo.rcMonitor.Top);
                            
                        var workArea = new Rect(
                            monitorInfo.rcWork.Left,
                            monitorInfo.rcWork.Top,
                            monitorInfo.rcWork.Right - monitorInfo.rcWork.Left,
                            monitorInfo.rcWork.Bottom - monitorInfo.rcWork.Top);
                            
                        // DPI情報を取得
                        uint dpiX = 96, dpiY = 96;
                        if (GetDpiForMonitor(hMonitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out dpiX, out dpiY) != 0)
                        {
                            dpiX = 96;
                            dpiY = 96;
                        }
                        
                        // モニター名を取得
                        var displayName = GetMonitorDisplayName(deviceName);
                        
                        // モニター情報を作成
                        var monitor = new MonitorInfo(
                            hMonitor,
                            displayName,
                            deviceName,
                            bounds,
                            workArea,
                            isPrimary,
                            dpiX,
                            dpiY);
                            
                        _monitors.Add(monitor);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "モニター情報の取得中にエラーが発生しました。");
                }
                
                return true;
            }
            
            // モニターを列挙
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, EnumMonitorsCallback, IntPtr.Zero);
            
            if (_monitors.Count == 0)
            {
                _logger?.LogWarning("モニターが検出されませんでした。デフォルトのモニター情報を使用します。");
                
                // デフォルトのモニター情報を作成
                var screenWidth = GetSystemMetrics(SM_CXSCREEN);
                var screenHeight = GetSystemMetrics(SM_CYSCREEN);
                
                var defaultMonitor = new MonitorInfo(
                    IntPtr.Zero,
                    "Default Monitor",
                    "DISPLAY1",
                    new Rect(0, 0, screenWidth, screenHeight),
                    new Rect(0, 0, screenWidth, screenHeight),
                    true,
                    96,
                    96);
                    
                _monitors.Add(defaultMonitor);
            }
            
            _logger?.LogDebug("モニター情報を更新しました。検出モニター数: {Count}", _monitors.Count);
            
            // イベントを発火
            MonitorChanged?.Invoke(this, new MonitorChangedEventArgs(
                MonitorChangeType.RefreshAll,
                null,
                _monitors));
        }
        
        /// <inheritdoc />
        public void StartMonitoring()
        {
            if (_isMonitoring)
                return;
                
            // メッセージ受信用のウィンドウを作成
            CreateMessageWindow();
            
            // ディスプレイ設定変更を監視するためのメッセージフィルターを登録
            RegisterDisplaySettingsNotification();
            
            _isMonitoring = true;
            
            _logger?.LogInformation("モニター監視を開始しました。");
        }
        
        /// <inheritdoc />
        public void StopMonitoring()
        {
            if (!_isMonitoring)
                return;
                
            // ディスプレイ設定変更の監視を解除
            UnregisterDisplaySettingsNotification();
            
            // メッセージウィンドウを破棄
            DestroyMessageWindow();
            
            _isMonitoring = false;
            
            _logger?.LogInformation("モニター監視を停止しました。");
        }
        
        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        
        /// <summary>
        /// リソースを解放します
        /// </summary>
        /// <param name="disposing">マネージドリソースを解放するかどうか</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;
                
            if (disposing)
            {
                StopMonitoring();
            }
            
            _disposed = true;
        }
        
        /// <summary>
        /// メッセージウィンドウを作成します
        /// </summary>
        private void CreateMessageWindow()
        {
            // メッセージウィンドウの作成と監視処理（省略）
        }
        
        /// <summary>
        /// メッセージウィンドウを破棄します
        /// </summary>
        private void DestroyMessageWindow()
        {
            // メッセージウィンドウの破棄処理（省略）
        }
        
        /// <summary>
        /// ディスプレイ設定変更通知を登録します
        /// </summary>
        private void RegisterDisplaySettingsNotification()
        {
            // ディスプレイ設定変更通知の登録処理（省略）
        }
        
        /// <summary>
        /// ディスプレイ設定変更通知を解除します
        /// </summary>
        private void UnregisterDisplaySettingsNotification()
        {
            // ディスプレイ設定変更通知の解除処理（省略）
        }
        
        /// <summary>
        /// モニターのディスプレイ名を取得します
        /// </summary>
        /// <param name="deviceName">デバイス名</param>
        /// <returns>ディスプレイ名</returns>
        private string GetMonitorDisplayName(string deviceName)
        {
            // レジストリからモニター情報を取得する処理（省略）
            return "Monitor";
        }
        
        /// <summary>
        /// ウィンドウプロシージャ
        /// </summary>
        private IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            // モニター関連のウィンドウメッセージ処理（省略）
            return DefWindowProc(hwnd, msg, wParam, lParam);
        }
    }
}
```

## オーバーレイウィンドウへの統合例
```csharp
namespace Baketa.UI.Overlay
{
    /// <summary>
    /// マルチモニター対応オーバーレイウィンドウマネージャー
    /// </summary>
    public class MultiMonitorOverlayWindowManager : IDisposable
    {
        private readonly ILogger? _logger;
        private readonly IMonitorManager _monitorManager;
        private readonly Dictionary<MonitorInfo, IOverlayWindow> _overlayWindows = new();
        private IntPtr _targetWindowHandle;
        private MonitorInfo? _activeMonitor;
        private readonly object _syncLock = new object();
        private bool _disposed;
        
        /// <summary>
        /// 新しいマルチモニター対応オーバーレイウィンドウマネージャーを初期化します
        /// </summary>
        /// <param name="monitorManager">モニターマネージャー</param>
        /// <param name="logger">ロガー</param>
        public MultiMonitorOverlayWindowManager(IMonitorManager monitorManager, ILogger? logger = null)
        {
            _monitorManager = monitorManager ?? throw new ArgumentNullException(nameof(monitorManager));
            _logger = logger;
            
            // モニター変更イベントを購読
            _monitorManager.MonitorChanged += OnMonitorChanged;
            
            // モニター監視を開始
            _monitorManager.StartMonitoring();
            
            _logger?.LogInformation("マルチモニター対応オーバーレイウィンドウマネージャーが初期化されました。");
        }
        
        /// <summary>
        /// ターゲットウィンドウを設定します
        /// </summary>
        /// <param name="windowHandle">ウィンドウハンドル</param>
        public void SetTargetWindow(IntPtr windowHandle)
        {
            lock (_syncLock)
            {
                if (_targetWindowHandle == windowHandle)
                    return;
                    
                _targetWindowHandle = windowHandle;
                
                if (_targetWindowHandle != IntPtr.Zero)
                {
                    // ウィンドウのモニターを取得
                    var monitor = _monitorManager.GetMonitorFromWindow(_targetWindowHandle);
                    UpdateActiveMonitor(monitor);
                }
                else
                {
                    // ターゲットウィンドウがない場合はすべてのオーバーレイを非表示
                    HideAllOverlays();
                }
            }
        }
        
        /// <summary>
        /// ターゲットウィンドウの位置変更を処理します
        /// </summary>
        public void HandleTargetWindowPositionChanged()
        {
            if (_targetWindowHandle == IntPtr.Zero)
                return;
                
            // ウィンドウの位置を取得
            if (GetWindowRect(_targetWindowHandle, out var rect))
            {
                var windowCenter = new Point(
                    (rect.Left + rect.Right) / 2,
                    (rect.Top + rect.Bottom) / 2);
                    
                // ウィンドウの中心点が含まれるモニターを取得
                var monitor = _monitorManager.GetMonitorFromPoint(windowCenter);
                UpdateActiveMonitor(monitor);
                
                // アクティブなオーバーレイの位置を更新
                if (_activeMonitor != null && _overlayWindows.TryGetValue(_activeMonitor, out var overlay))
                {
                    overlay.TargetWindowHandle = _targetWindowHandle;
                    overlay.AdjustToTargetWindow();
                }
            }
        }
        
        /// <summary>
        /// テキストコンテンツを更新します
        /// </summary>
        /// <param name="content">表示するコンテンツ</param>
        public void UpdateContent(IVisual content)
        {
            if (_activeMonitor != null && _overlayWindows.TryGetValue(_activeMonitor, out var overlay))
            {
                overlay.UpdateContent(content);
            }
        }
        
        /// <summary>
        /// すべてのオーバーレイを表示します
        /// </summary>
        public void ShowAllOverlays()
        {
            foreach (var overlay in _overlayWindows.Values)
            {
                overlay.Show();
            }
        }
        
        /// <summary>
        /// すべてのオーバーレイを非表示にします
        /// </summary>
        public void HideAllOverlays()
        {
            foreach (var overlay in _overlayWindows.Values)
            {
                overlay.Hide();
            }
        }
        
        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        
        /// <summary>
        /// リソースを解放します
        /// </summary>
        /// <param name="disposing">マネージドリソースを解放するかどうか</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;
                
            if (disposing)
            {
                // イベント購読を解除
                _monitorManager.MonitorChanged -= OnMonitorChanged;
                
                // すべてのオーバーレイウィンドウを破棄
                foreach (var overlay in _overlayWindows.Values)
                {
                    overlay.Dispose();
                }
                
                _overlayWindows.Clear();
            }
            
            _disposed = true;
        }
        
        /// <summary>
        /// アクティブモニターを更新します
        /// </summary>
        /// <param name="monitor">モニター情報</param>
        private void UpdateActiveMonitor(MonitorInfo monitor)
        {
            if (_activeMonitor == monitor)
                return;
                
            // 以前のアクティブオーバーレイを非表示
            if (_activeMonitor != null && _overlayWindows.TryGetValue(_activeMonitor, out var previousOverlay))
            {
                previousOverlay.Hide();
            }
            
            _activeMonitor = monitor;
            
            // モニター用のオーバーレイが存在しない場合は作成
            if (_activeMonitor != null && !_overlayWindows.ContainsKey(_activeMonitor))
            {
                CreateOverlayForMonitor(_activeMonitor);
            }
            
            // 新しいアクティブオーバーレイを表示
            if (_activeMonitor != null && _overlayWindows.TryGetValue(_activeMonitor, out var overlay))
            {
                overlay.TargetWindowHandle = _targetWindowHandle;
                overlay.AdjustToTargetWindow();
                overlay.Show();
            }
            
            _logger?.LogDebug("アクティブモニターを更新しました: {MonitorName}", _activeMonitor?.Name);
        }
        
        /// <summary>
        /// モニター用のオーバーレイを作成します
        /// </summary>
        /// <param name="monitor">モニター情報</param>
        private void CreateOverlayForMonitor(MonitorInfo monitor)
        {
            // オーバーレイウィンドウを作成
            var overlay = new WindowsOverlayWindow(_logger);
            _overlayWindows[monitor] = overlay;
            
            _logger?.LogInformation("モニター {MonitorName} 用のオーバーレイを作成しました。", monitor.Name);
        }
        
        /// <summary>
        /// モニター変更イベントハンドラー
        /// </summary>
        private void OnMonitorChanged(object? sender, MonitorChangedEventArgs e)
        {
            lock (_syncLock)
            {
                switch (e.ChangeType)
                {
                    case MonitorChangeType.Added:
                        if (e.AffectedMonitor != null)
                        {
                            _logger?.LogInformation("モニターが追加されました: {MonitorName}", e.AffectedMonitor.Name);
                        }
                        break;
                        
                    case MonitorChangeType.Removed:
                        if (e.AffectedMonitor != null)
                        {
                            _logger?.LogInformation("モニターが削除されました: {MonitorName}", e.AffectedMonitor.Name);
                            
                            // 削除されたモニターのオーバーレイを破棄
                            if (_overlayWindows.TryGetValue(e.AffectedMonitor, out var overlay))
                            {
                                overlay.Dispose();
                                _overlayWindows.Remove(e.AffectedMonitor);
                            }
                            
                            // アクティブモニターが削除された場合は新しいモニターを選択
                            if (_activeMonitor == e.AffectedMonitor)
                            {
                                _activeMonitor = null;
                                
                                if (_targetWindowHandle != IntPtr.Zero)
                                {
                                    var newMonitor = _monitorManager.GetMonitorFromWindow(_targetWindowHandle);
                                    UpdateActiveMonitor(newMonitor);
                                }
                            }
                        }
                        break;
                        
                    case MonitorChangeType.Changed:
                    case MonitorChangeType.PrimaryChanged:
                    case MonitorChangeType.RefreshAll:
                        _logger?.LogInformation("モニター設定が変更されました。");
                        
                        // オーバーレイの再構築が必要かどうかを判断
                        var rebuildRequired = NeedRebuildOverlays(e);
                        
                        if (rebuildRequired)
                        {
                            RebuildAllOverlays();
                        }
                        else if (_activeMonitor != null)
                        {
                            // アクティブオーバーレイの位置を更新
                            if (_overlayWindows.TryGetValue(_activeMonitor, out var activeOverlay))
                            {
                                activeOverlay.AdjustToTargetWindow();
                            }
                        }
                        break;
                }
            }
        }
        
        /// <summary>
        /// オーバーレイの再構築が必要かどうかを判断します
        /// </summary>
        /// <param name="e">モニター変更イベント引数</param>
        /// <returns>再構築が必要ならtrue</returns>
        private bool NeedRebuildOverlays(MonitorChangedEventArgs e)
        {
            // モニター変更に基づく再構築判断ロジック（省略）
            return false;
        }
        
        /// <summary>
        /// すべてのオーバーレイを再構築します
        /// </summary>
        private void RebuildAllOverlays()
        {
            // 既存のオーバーレイをすべて破棄
            foreach (var overlay in _overlayWindows.Values)
            {
                overlay.Dispose();
            }
            
            _overlayWindows.Clear();
            _activeMonitor = null;
            
            // ターゲットウィンドウがある場合は新しいアクティブモニターを設定
            if (_targetWindowHandle != IntPtr.Zero)
            {
                var monitor = _monitorManager.GetMonitorFromWindow(_targetWindowHandle);
                UpdateActiveMonitor(monitor);
            }
            
            _logger?.LogInformation("すべてのオーバーレイを再構築しました。");
        }
    }
}
```

## 実装上の注意点
- 異なるDPI設定のモニター間での座標変換の正確性
- モニターの接続・切断や設定変更の適切な検出と対応
- オーバーレイウィンドウリソースの効率的な管理（不要なウィンドウ作成の回避）
- ウィンドウ移動追跡のパフォーマンス最適化
- Win32 APIのP/Invoke定義の正確な実装
- マルチモニター環境でのゲームフルスクリーンモードの適切な処理
- ユーザーカスタム設定とモニター固有設定の適切な統合
- スレッドセーフな実装と適切な同期メカニズムの使用

## 関連Issue/参考
- 親Issue: #11 オーバーレイウィンドウ
- 依存Issue: #11-1 透過ウィンドウとクリックスルー機能の実装
- 関連Issue: #11-2 オーバーレイ位置とサイズの管理システムの実装
- 参照: E:\dev\Baketa\docs\3-architecture\ui-system\multi-monitor-support.md
- 参照: E:\dev\Baketa\docs\2-development\platform-interop\windows-dpi.md
- 参照: E:\dev\Baketa\docs\2-development\coding-standards\csharp-standards.md (4.2 リソース解放とDisposable)

## マイルストーン
マイルストーン3: 翻訳とUI

## ラベル
- `type: feature`
- `priority: medium`
- `component: ui`
