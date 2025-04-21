# Issue 10-3: システムトレイ機能の実装

## 概要
Baketaアプリケーションのシステムトレイ機能を実装します。これにより、アプリケーションがバックグラウンドで実行され、ユーザーが必要なときに簡単にアクセスできるようになります。

## 目的・理由
システムトレイ機能は以下の理由から必要です：

1. ゲームプレイ中に常にBaketaのメインウィンドウを表示させる必要をなくし、ゲーム体験を妨げない
2. アプリケーションの主要機能に素早くアクセスできるメニューを提供する
3. アプリケーションの現在の状態（キャプチャ状態、翻訳エンジンなど）を視覚的に確認できるようにする
4. システムリソースの使用を最適化しながら、バックグラウンドでの継続的な動作を実現する

## 詳細
- システムトレイアイコンとコンテキストメニューの実装
- トレイアイコン状態の動的更新機能の実装
- ホットキーとの連携機能の実装
- アプリケーション状態通知の実装

## タスク分解
- [ ] トレイアイコン基本機能
  - [ ] `SystemTrayManager`クラスの設計と実装
  - [ ] トレイアイコンの表示と非表示の制御
  - [ ] アイコンリソースの管理
- [ ] コンテキストメニュー
  - [ ] コンテキストメニュー構造の設計
  - [ ] メニュー項目とコマンドの連携
  - [ ] 動的メニュー項目の更新機能
- [ ] 状態表示
  - [ ] アイコン状態の切り替え機能
  - [ ] ツールチップ表示の実装
  - [ ] 通知バルーンの実装
- [ ] イベント連携
  - [ ] キャプチャ状態変更イベントとの連携
  - [ ] 翻訳状態変更イベントとの連携
  - [ ] エラー状態とのマッピング
- [ ] トレイ通知
  - [ ] トレイ通知メッセージの表示
  - [ ] 通知クリック時のアクション
  - [ ] 通知の優先度管理
- [ ] メインウィンドウ連携
  - [ ] メインウィンドウの最小化/トレイ化
  - [ ] トレイからのウィンドウ復元
  - [ ] ウィンドウ状態保存と復元
- [ ] 設定統合
  - [ ] トレイ動作のカスタマイズ設定
  - [ ] 起動時のトレイ表示設定
  - [ ] 通知設定
- [ ] 単体テストの実装

## インターフェース設計案
```csharp
namespace Baketa.UI.SystemTray
{
    /// <summary>
    /// システムトレイマネージャーインターフェース
    /// </summary>
    public interface ISystemTrayManager : IDisposable
    {
        /// <summary>
        /// トレイアイコンが表示されているかどうか
        /// </summary>
        bool IsVisible { get; }
        
        /// <summary>
        /// 現在のトレイアイコン状態
        /// </summary>
        TrayIconState IconState { get; }
        
        /// <summary>
        /// トレイアイコンを表示します
        /// </summary>
        void Show();
        
        /// <summary>
        /// トレイアイコンを非表示にします
        /// </summary>
        void Hide();
        
        /// <summary>
        /// トレイアイコンの状態を設定します
        /// </summary>
        /// <param name="state">設定する状態</param>
        void SetIconState(TrayIconState state);
        
        /// <summary>
        /// ツールチップテキストを設定します
        /// </summary>
        /// <param name="text">ツールチップテキスト</param>
        void SetTooltip(string text);
        
        /// <summary>
        /// 通知バルーンを表示します
        /// </summary>
        /// <param name="title">タイトル</param>
        /// <param name="message">メッセージ</param>
        /// <param name="icon">アイコン</param>
        /// <param name="timeout">表示時間（ミリ秒）</param>
        void ShowNotification(string title, string message, TrayNotificationIcon icon = TrayNotificationIcon.Info, int timeout = 3000);
        
        /// <summary>
        /// コンテキストメニューの項目を更新します
        /// </summary>
        /// <param name="itemId">項目ID</param>
        /// <param name="enabled">有効かどうか</param>
        /// <param name="text">テキスト（nullの場合は変更なし）</param>
        /// <param name="checked">チェック状態（nullの場合は変更なし）</param>
        void UpdateMenuItem(string itemId, bool enabled, string? text = null, bool? @checked = null);
    }
    
    /// <summary>
    /// システムトレイマネージャー実装クラス
    /// </summary>
    public class SystemTrayManager : ISystemTrayManager
    {
        // プライベートフィールド
        private readonly IEventAggregator _eventAggregator;
        private readonly ILogger? _logger;
        private readonly IWindowManager _windowManager;
        private readonly ICaptureService _captureService;
        private readonly ITranslationService _translationService;
        private readonly IDisposable[] _subscriptions;
        private readonly NotifyIcon _notifyIcon;
        private readonly ContextMenu _contextMenu;
        private readonly Dictionary<string, MenuItem> _menuItems = new();
        private TrayIconState _iconState = TrayIconState.Idle;
        private bool _disposed;
        
        /// <summary>
        /// 新しいシステムトレイマネージャーを初期化します
        /// </summary>
        /// <param name="eventAggregator">イベント集約器</param>
        /// <param name="windowManager">ウィンドウマネージャー</param>
        /// <param name="captureService">キャプチャサービス</param>
        /// <param name="translationService">翻訳サービス</param>
        /// <param name="logger">ロガー</param>
        public SystemTrayManager(
            IEventAggregator eventAggregator,
            IWindowManager windowManager,
            ICaptureService captureService,
            ITranslationService translationService,
            ILogger? logger = null)
        {
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
            _windowManager = windowManager ?? throw new ArgumentNullException(nameof(windowManager));
            _captureService = captureService ?? throw new ArgumentNullException(nameof(captureService));
            _translationService = translationService ?? throw new ArgumentNullException(nameof(translationService));
            _logger = logger;
            
            // コンテキストメニューの初期化
            _contextMenu = InitializeContextMenu();
            
            // トレイアイコンの初期化
            _notifyIcon = new NotifyIcon
            {
                Icon = LoadIconForState(TrayIconState.Idle),
                Text = "Baketa - ゲーム翻訳オーバーレイ",
                Visible = false,
                ContextMenu = _contextMenu
            };
            
            // イベント購読
            _subscriptions = new[]
            {
                _eventAggregator.Subscribe<CaptureStatusChangedEvent>(OnCaptureStatusChanged),
                _eventAggregator.Subscribe<TranslationCompletedEvent>(OnTranslationCompleted),
                _eventAggregator.Subscribe<TranslationErrorEvent>(OnTranslationError)
            };
            
            // ダブルクリックイベントの設定
            _notifyIcon.DoubleClick += OnNotifyIconDoubleClick;
            
            _logger?.LogInformation("システムトレイマネージャーが初期化されました");
        }
        
        /// <inheritdoc />
        public bool IsVisible => _notifyIcon.Visible;
        
        /// <inheritdoc />
        public TrayIconState IconState => _iconState;
        
        /// <inheritdoc />
        public void Show()
        {
            _notifyIcon.Visible = true;
            _logger?.LogDebug("システムトレイアイコンが表示されました");
        }
        
        /// <inheritdoc />
        public void Hide()
        {
            _notifyIcon.Visible = false;
            _logger?.LogDebug("システムトレイアイコンが非表示になりました");
        }
        
        /// <inheritdoc />
        public void SetIconState(TrayIconState state)
        {
            if (_iconState == state)
                return;
                
            _iconState = state;
            _notifyIcon.Icon = LoadIconForState(state);
            
            string stateText = state switch
            {
                TrayIconState.Idle => "アイドル状態",
                TrayIconState.Capturing => "キャプチャ中",
                TrayIconState.Translating => "翻訳中",
                TrayIconState.Error => "エラー状態",
                _ => "不明な状態"
            };
            
            _logger?.LogDebug("システムトレイアイコンの状態が変更されました: {State}", stateText);
        }
        
        /// <inheritdoc />
        public void SetTooltip(string text)
        {
            _notifyIcon.Text = text;
        }
        
        /// <inheritdoc />
        public void ShowNotification(string title, string message, TrayNotificationIcon icon = TrayNotificationIcon.Info, int timeout = 3000)
        {
            var iconType = icon switch
            {
                TrayNotificationIcon.Info => ToolTipIcon.Info,
                TrayNotificationIcon.Warning => ToolTipIcon.Warning,
                TrayNotificationIcon.Error => ToolTipIcon.Error,
                TrayNotificationIcon.None => ToolTipIcon.None,
                _ => ToolTipIcon.Info
            };
            
            _notifyIcon.ShowBalloonTip(timeout, title, message, iconType);
            _logger?.LogDebug("システムトレイ通知が表示されました: {Title}", title);
        }
        
        /// <inheritdoc />
        public void UpdateMenuItem(string itemId, bool enabled, string? text = null, bool? @checked = null)
        {
            if (_menuItems.TryGetValue(itemId, out var menuItem))
            {
                menuItem.Enabled = enabled;
                
                if (text != null)
                    menuItem.Text = text;
                    
                if (@checked.HasValue && menuItem is CheckedMenuItem checkedMenuItem)
                    checkedMenuItem.Checked = @checked.Value;
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
                // サブスクリプションの解除
                foreach (var subscription in _subscriptions)
                    subscription.Dispose();
                    
                // トレイアイコンの解放
                _notifyIcon.DoubleClick -= OnNotifyIconDoubleClick;
                _notifyIcon.Dispose();
            }
            
            _disposed = true;
        }
        
        /// <summary>
        /// コンテキストメニューを初期化します
        /// </summary>
        /// <returns>初期化されたコンテキストメニュー</returns>
        private ContextMenu InitializeContextMenu()
        {
            var menu = new ContextMenu();
            
            // メインウィンドウを表示/非表示
            var showHideItem = new MenuItem { Text = "メインウィンドウを表示" };
            showHideItem.Click += OnShowHideMainWindowClick;
            menu.MenuItems.Add(showHideItem);
            _menuItems["ShowHide"] = showHideItem;
            
            // セパレータ
            menu.MenuItems.Add(new MenuItem { Text = "-" });
            
            // キャプチャ開始/停止
            var captureItem = new MenuItem { Text = "キャプチャ開始" };
            captureItem.Click += OnCaptureStartStopClick;
            menu.MenuItems.Add(captureItem);
            _menuItems["Capture"] = captureItem;
            
            // 翻訳エンジン選択サブメニュー
            var translationEngineItem = new MenuItem { Text = "翻訳エンジン" };
            menu.MenuItems.Add(translationEngineItem);
            _menuItems["TranslationEngine"] = translationEngineItem;
            
            // 翻訳エンジンオプションを後で動的に追加
            
            // セパレータ
            menu.MenuItems.Add(new MenuItem { Text = "-" });
            
            // 設定
            var settingsItem = new MenuItem { Text = "設定..." };
            settingsItem.Click += OnSettingsClick;
            menu.MenuItems.Add(settingsItem);
            _menuItems["Settings"] = settingsItem;
            
            // セパレータ
            menu.MenuItems.Add(new MenuItem { Text = "-" });
            
            // 終了
            var exitItem = new MenuItem { Text = "終了" };
            exitItem.Click += OnExitClick;
            menu.MenuItems.Add(exitItem);
            _menuItems["Exit"] = exitItem;
            
            return menu;
        }
        
        /// <summary>
        /// 状態に対応するアイコンを読み込みます
        /// </summary>
        /// <param name="state">アイコン状態</param>
        /// <returns>読み込まれたアイコン</returns>
        private Icon LoadIconForState(TrayIconState state)
        {
            string iconName = state switch
            {
                TrayIconState.Idle => "baketa-idle.ico",
                TrayIconState.Capturing => "baketa-capturing.ico",
                TrayIconState.Translating => "baketa-translating.ico",
                TrayIconState.Error => "baketa-error.ico",
                _ => "baketa-idle.ico"
            };
            
            string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", iconName);
            
            try
            {
                return new Icon(iconPath);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "システムトレイアイコンの読み込みに失敗しました: {IconPath}", iconPath);
                return SystemIcons.Application;
            }
        }
        
        // イベントハンドラー実装
        private async Task OnCaptureStatusChanged(CaptureStatusChangedEvent @event) { /* 実装 */ }
        private async Task OnTranslationCompleted(TranslationCompletedEvent @event) { /* 実装 */ }
        private async Task OnTranslationError(TranslationErrorEvent @event) { /* 実装 */ }
        private void OnNotifyIconDoubleClick(object? sender, EventArgs e) { /* 実装 */ }
        private void OnShowHideMainWindowClick(object? sender, EventArgs e) { /* 実装 */ }
        private void OnCaptureStartStopClick(object? sender, EventArgs e) { /* 実装 */ }
        private void OnSettingsClick(object? sender, EventArgs e) { /* 実装 */ }
        private void OnExitClick(object? sender, EventArgs e) { /* 実装 */ }
    }
    
    /// <summary>
    /// トレイアイコン状態
    /// </summary>
    public enum TrayIconState
    {
        /// <summary>
        /// アイドル状態
        /// </summary>
        Idle,
        
        /// <summary>
        /// キャプチャ中
        /// </summary>
        Capturing,
        
        /// <summary>
        /// 翻訳中
        /// </summary>
        Translating,
        
        /// <summary>
        /// エラー状態
        /// </summary>
        Error
    }
    
    /// <summary>
    /// トレイ通知アイコン
    /// </summary>
    public enum TrayNotificationIcon
    {
        /// <summary>
        /// アイコンなし
        /// </summary>
        None,
        
        /// <summary>
        /// 情報アイコン
        /// </summary>
        Info,
        
        /// <summary>
        /// 警告アイコン
        /// </summary>
        Warning,
        
        /// <summary>
        /// エラーアイコン
        /// </summary>
        Error
    }
}
```

## Avalonia統合設計案
```csharp
namespace Baketa.UI.SystemTray
{
    /// <summary>
    /// Avalonia UIとシステムトレイを統合するサービス
    /// </summary>
    public class AvaloniaTrayService : IDisposable
    {
        private readonly ISystemTrayManager _trayManager;
        private readonly IWindowManager _windowManager;
        private readonly IEventAggregator _eventAggregator;
        private readonly ILogger? _logger;
        private readonly IDisposable[] _subscriptions;
        private bool _mainWindowVisible = true;
        private bool _disposed;
        
        /// <summary>
        /// 新しいAvalonia UIトレイサービスを初期化します
        /// </summary>
        /// <param name="trayManager">システムトレイマネージャー</param>
        /// <param name="windowManager">ウィンドウマネージャー</param>
        /// <param name="eventAggregator">イベント集約器</param>
        /// <param name="logger">ロガー</param>
        public AvaloniaTrayService(
            ISystemTrayManager trayManager,
            IWindowManager windowManager,
            IEventAggregator eventAggregator,
            ILogger? logger = null)
        {
            _trayManager = trayManager ?? throw new ArgumentNullException(nameof(trayManager));
            _windowManager = windowManager ?? throw new ArgumentNullException(nameof(windowManager));
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
            _logger = logger;
            
            // イベント購読
            _subscriptions = new[]
            {
                _eventAggregator.Subscribe<WindowStateChangedEvent>(OnWindowStateChanged),
                _eventAggregator.Subscribe<MinimizeToTrayRequestedEvent>(OnMinimizeToTrayRequested),
                _eventAggregator.Subscribe<TrayNotificationRequestedEvent>(OnTrayNotificationRequested)
            };
            
            // トレイアイコンを表示
            _trayManager.Show();
            
            _logger?.LogInformation("Avalonia UIトレイサービスが初期化されました");
        }
        
        /// <summary>
        /// リソースを解放します
        /// </summary>
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
                // サブスクリプションの解除
                foreach (var subscription in _subscriptions)
                    subscription.Dispose();
                    
                // トレイアイコンの非表示
                _trayManager.Hide();
            }
            
            _disposed = true;
        }
        
        /// <summary>
        /// ウィンドウ状態変更イベントハンドラー
        /// </summary>
        /// <param name="event">イベント</param>
        private async Task OnWindowStateChanged(WindowStateChangedEvent @event)
        {
            if (@event.WindowType == WindowType.Main)
            {
                if (@event.WindowState == WindowState.Minimized)
                {
                    // 設定に応じてトレイに最小化
                    bool minimizeToTray = true; // TODO: 設定から取得
                    
                    if (minimizeToTray)
                    {
                        _mainWindowVisible = false;
                        _windowManager.HideMainWindow();
                        _trayManager.UpdateMenuItem("ShowHide", true, "メインウィンドウを表示");
                        
                        bool showNotification = true; // TODO: 設定から取得
                        if (showNotification)
                        {
                            _trayManager.ShowNotification(
                                "Baketa",
                                "Baketaはバックグラウンドで実行中です。\nタスクトレイアイコンをダブルクリックすると元に戻ります。",
                                TrayNotificationIcon.Info,
                                5000);
                        }
                    }
                }
                else
                {
                    _mainWindowVisible = true;
                    _trayManager.UpdateMenuItem("ShowHide", true, "メインウィンドウを非表示");
                }
            }
        }
        
        /// <summary>
        /// トレイに最小化リクエストイベントハンドラー
        /// </summary>
        /// <param name="event">イベント</param>
        private async Task OnMinimizeToTrayRequested(MinimizeToTrayRequestedEvent @event)
        {
            _mainWindowVisible = false;
            _windowManager.HideMainWindow();
            _trayManager.UpdateMenuItem("ShowHide", true, "メインウィンドウを表示");
        }
        
        /// <summary>
        /// トレイ通知リクエストイベントハンドラー
        /// </summary>
        /// <param name="event">イベント</param>
        private async Task OnTrayNotificationRequested(TrayNotificationRequestedEvent @event)
        {
            _trayManager.ShowNotification(@event.Title, @event.Message, @event.Icon, @event.Timeout);
        }
        
        // トレイメニュー処理メソッドの追加実装
    }
}
```

## 実装上の注意点
- .NETとAvaloniaの制約を考慮したトレイアイコン実装を行う
- メインウィンドウとトレイアイコンの状態同期を確実に行う
- リソースリークを防ぐため、トレイアイコンの適切な解放処理を実装する
- マルチプラットフォームの可能性を考慮し、プラットフォーム固有コードを分離する
- アプリケーション終了時にトレイアイコンを適切に削除する
- トレイアイコンの状態変更はスレッドセーフに実装する
- ユーザー設定に基づくトレイ動作のカスタマイズを可能にする

## 関連Issue/参考
- 親Issue: #10 Avalonia UIフレームワーク完全実装
- 依存Issue: #10-1 ReactiveUIベースのMVVMフレームワーク実装
- 関連Issue: #10-4 ホットキーマネージャーの実装
- 参照: E:\dev\Baketa\docs\3-architecture\ui-system\tray-icon-integration.md
- 参照: E:\dev\Baketa\docs\2-development\coding-standards\csharp-standards.md (4.2 リソース解放とDisposable)

## マイルストーン
マイルストーン3: 翻訳とUI

## ラベル
- `type: feature`
- `priority: high`
- `component: ui`
