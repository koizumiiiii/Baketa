# Issue 10-4: ホットキーマネージャーの実装

## 概要
Baketaアプリケーションのグローバルホットキー機能を実装します。これにより、ユーザーはゲームプレイ中でもキーボードショートカットを使用してアプリケーションの主要機能を制御できるようになります。

## 目的・理由
ホットキー機能は以下の理由から重要です：

1. ゲームプレイ中にメインウィンドウを操作せずに機能を制御できる
2. 翻訳表示のON/OFF、キャプチャの開始/停止などの操作を素早く実行できる
3. ユーザーの作業効率を向上させる
4. ゲームプレイの中断を最小限に抑えながらBaketaの機能を利用できる

## 詳細
- グローバルホットキー登録と監視メカニズムの実装
- ホットキーとアクションのマッピング機能の実装
- カスタマイズ可能なホットキー設定システムの実装
- キー競合検出と解決メカニズムの実装

## タスク分解
- [ ] ホットキーマネージャー基盤
  - [ ] `IHotkeyManager`インターフェースの設計
  - [ ] `HotkeyManager`クラスの実装
  - [ ] Windows低レベルキーボードフックの実装
- [ ] ホットキー定義
  - [ ] `Hotkey`構造体の設計
  - [ ] キー組み合わせとシリアライズのサポート
  - [ ] 表示名生成機能の実装
- [ ] ホットキーアクション
  - [ ] `HotkeyAction`クラスの設計
  - [ ] アクション実行メカニズムの実装
  - [ ] アクション権限管理の実装
- [ ] ホットキー設定
  - [ ] 設定保存・読み込み機能の実装
  - [ ] デフォルトホットキーの定義
  - [ ] ホットキーリセット機能の実装
- [ ] 競合検出
  - [ ] ホットキー競合検出ロジックの実装
  - [ ] 競合解決UIの実装
  - [ ] 優先度に基づく競合解決の実装
- [ ] ゲーム検出と状態管理
  - [ ] アクティブウィンドウ監視機能の実装
  - [ ] ゲームプロファイルとの連携
  - [ ] 状態に応じたホットキー有効/無効切り替え
- [ ] UI統合
  - [ ] ホットキー設定UIの設計
  - [ ] キー割り当て変更UIの実装
  - [ ] 視覚的なホットキー表示の実装
- [ ] デバッグとログ
  - [ ] ホットキー検出のログ機能の実装
  - [ ] 問題診断機能の実装
- [ ] 単体テストの実装

## インターフェース設計案
```csharp
namespace Baketa.UI.Hotkeys
{
    /// <summary>
    /// ホットキーマネージャーインターフェース
    /// </summary>
    public interface IHotkeyManager : IDisposable
    {
        /// <summary>
        /// ホットキーが有効かどうか
        /// </summary>
        bool IsEnabled { get; }
        
        /// <summary>
        /// 登録されているホットキーのコレクション
        /// </summary>
        IReadOnlyDictionary<string, RegisteredHotkey> RegisteredHotkeys { get; }
        
        /// <summary>
        /// ホットキーを登録します
        /// </summary>
        /// <param name="id">ホットキーID</param>
        /// <param name="hotkey">ホットキー定義</param>
        /// <param name="action">実行アクション</param>
        /// <param name="description">説明</param>
        /// <returns>登録が成功したかどうか</returns>
        bool RegisterHotkey(string id, Hotkey hotkey, Action<HotkeyEventArgs> action, string description);
        
        /// <summary>
        /// ホットキーの登録を解除します
        /// </summary>
        /// <param name="id">ホットキーID</param>
        /// <returns>解除が成功したかどうか</returns>
        bool UnregisterHotkey(string id);
        
        /// <summary>
        /// 指定したIDのホットキーを更新します
        /// </summary>
        /// <param name="id">ホットキーID</param>
        /// <param name="hotkey">新しいホットキー定義</param>
        /// <returns>更新が成功したかどうか</returns>
        bool UpdateHotkey(string id, Hotkey hotkey);
        
        /// <summary>
        /// すべてのホットキーを有効にします
        /// </summary>
        void EnableHotkeys();
        
        /// <summary>
        /// すべてのホットキーを無効にします
        /// </summary>
        void DisableHotkeys();
        
        /// <summary>
        /// 特定のホットキーを有効/無効にします
        /// </summary>
        /// <param name="id">ホットキーID</param>
        /// <param name="enabled">有効にするかどうか</param>
        /// <returns>操作が成功したかどうか</returns>
        bool SetHotkeyEnabled(string id, bool enabled);
        
        /// <summary>
        /// ホットキー設定を保存します
        /// </summary>
        /// <returns>保存が成功したかどうか</returns>
        Task<bool> SaveSettingsAsync();
        
        /// <summary>
        /// ホットキー設定を読み込みます
        /// </summary>
        /// <returns>読み込みが成功したかどうか</returns>
        Task<bool> LoadSettingsAsync();
        
        /// <summary>
        /// ホットキー設定をデフォルトにリセットします
        /// </summary>
        /// <returns>リセットが成功したかどうか</returns>
        Task<bool> ResetToDefaultsAsync();
        
        /// <summary>
        /// 競合するホットキーを検出します
        /// </summary>
        /// <param name="hotkey">検査するホットキー</param>
        /// <param name="excludeId">除外するホットキーID</param>
        /// <returns>競合するホットキーのリスト</returns>
        IReadOnlyList<RegisteredHotkey> DetectConflicts(Hotkey hotkey, string? excludeId = null);
    }
    
    /// <summary>
    /// ホットキーを表す構造体
    /// </summary>
    public readonly struct Hotkey : IEquatable<Hotkey>
    {
        /// <summary>
        /// キーコード
        /// </summary>
        public readonly Keys Key { get; }
        
        /// <summary>
        /// 修飾キー
        /// </summary>
        public readonly ModifierKeys Modifiers { get; }
        
        /// <summary>
        /// 新しいホットキーを初期化します
        /// </summary>
        /// <param name="key">キーコード</param>
        /// <param name="modifiers">修飾キー</param>
        public Hotkey(Keys key, ModifierKeys modifiers)
        {
            Key = key;
            Modifiers = modifiers;
        }
        
        /// <summary>
        /// ホットキーの表示名を取得します
        /// </summary>
        /// <returns>表示名</returns>
        public string GetDisplayName()
        {
            var parts = new List<string>();
            
            if ((Modifiers & ModifierKeys.Control) != 0)
                parts.Add("Ctrl");
                
            if ((Modifiers & ModifierKeys.Alt) != 0)
                parts.Add("Alt");
                
            if ((Modifiers & ModifierKeys.Shift) != 0)
                parts.Add("Shift");
                
            if ((Modifiers & ModifierKeys.Windows) != 0)
                parts.Add("Win");
                
            parts.Add(Key.ToString());
            
            return string.Join("+", parts);
        }
        
        /// <summary>
        /// 文字列からホットキーを解析します
        /// </summary>
        /// <param name="hotkeyString">ホットキー文字列</param>
        /// <returns>解析されたホットキー</returns>
        public static Hotkey Parse(string hotkeyString)
        {
            if (string.IsNullOrWhiteSpace(hotkeyString))
                throw new ArgumentException("ホットキー文字列が空です。", nameof(hotkeyString));
                
            var parts = hotkeyString.Split('+');
            if (parts.Length < 1)
                throw new FormatException("無効なホットキー形式です。");
                
            var modifiers = ModifierKeys.None;
            Keys key = Keys.None;
            
            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i].Trim();
                
                if (i == parts.Length - 1)
                {
                    // 最後の部分はキーコード
                    if (!Enum.TryParse(part, true, out key))
                        throw new FormatException($"無効なキーコード: {part}");
                }
                else
                {
                    // 修飾キー
                    switch (part.ToLowerInvariant())
                    {
                        case "ctrl":
                        case "control":
                            modifiers |= ModifierKeys.Control;
                            break;
                            
                        case "alt":
                            modifiers |= ModifierKeys.Alt;
                            break;
                            
                        case "shift":
                            modifiers |= ModifierKeys.Shift;
                            break;
                            
                        case "win":
                        case "windows":
                            modifiers |= ModifierKeys.Windows;
                            break;
                            
                        default:
                            throw new FormatException($"無効な修飾キー: {part}");
                    }
                }
            }
            
            return new Hotkey(key, modifiers);
        }
        
        /// <override />
        public bool Equals(Hotkey other)
        {
            return Key == other.Key && Modifiers == other.Modifiers;
        }
        
        /// <override />
        public override bool Equals(object? obj)
        {
            return obj is Hotkey other && Equals(other);
        }
        
        /// <override />
        public override int GetHashCode()
        {
            return HashCode.Combine(Key, Modifiers);
        }
        
        /// <override />
        public override string ToString()
        {
            return GetDisplayName();
        }
        
        /// <summary>
        /// 等価演算子
        /// </summary>
        public static bool operator ==(Hotkey left, Hotkey right)
        {
            return left.Equals(right);
        }
        
        /// <summary>
        /// 非等価演算子
        /// </summary>
        public static bool operator !=(Hotkey left, Hotkey right)
        {
            return !left.Equals(right);
        }
    }
    
    /// <summary>
    /// 修飾キー
    /// </summary>
    [Flags]
    public enum ModifierKeys
    {
        /// <summary>
        /// なし
        /// </summary>
        None = 0,
        
        /// <summary>
        /// Alt
        /// </summary>
        Alt = 1,
        
        /// <summary>
        /// Control
        /// </summary>
        Control = 2,
        
        /// <summary>
        /// Shift
        /// </summary>
        Shift = 4,
        
        /// <summary>
        /// Windows
        /// </summary>
        Windows = 8
    }
    
    /// <summary>
    /// 登録済みホットキー情報
    /// </summary>
    public class RegisteredHotkey
    {
        /// <summary>
        /// ホットキーID
        /// </summary>
        public string Id { get; }
        
        /// <summary>
        /// ホットキー定義
        /// </summary>
        public Hotkey Hotkey { get; private set; }
        
        /// <summary>
        /// ホットキー説明
        /// </summary>
        public string Description { get; }
        
        /// <summary>
        /// ホットキーを実行するアクション
        /// </summary>
        public Action<HotkeyEventArgs> Action { get; }
        
        /// <summary>
        /// ホットキーが有効かどうか
        /// </summary>
        public bool IsEnabled { get; set; }
        
        /// <summary>
        /// 優先度
        /// </summary>
        public int Priority { get; set; }
        
        /// <summary>
        /// ホットキー登録時刻
        /// </summary>
        public DateTime RegisteredAt { get; }
        
        /// <summary>
        /// 新しい登録済みホットキーを初期化します
        /// </summary>
        /// <param name="id">ホットキーID</param>
        /// <param name="hotkey">ホットキー定義</param>
        /// <param name="action">実行アクション</param>
        /// <param name="description">説明</param>
        /// <param name="isEnabled">有効かどうか</param>
        /// <param name="priority">優先度</param>
        public RegisteredHotkey(
            string id,
            Hotkey hotkey,
            Action<HotkeyEventArgs> action,
            string description,
            bool isEnabled = true,
            int priority = 0)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Hotkey = hotkey;
            Action = action ?? throw new ArgumentNullException(nameof(action));
            Description = description ?? string.Empty;
            IsEnabled = isEnabled;
            Priority = priority;
            RegisteredAt = DateTime.Now;
        }
        
        /// <summary>
        /// ホットキーを更新します
        /// </summary>
        /// <param name="hotkey">新しいホットキー定義</param>
        public void UpdateHotkey(Hotkey hotkey)
        {
            Hotkey = hotkey;
        }
    }
    
    /// <summary>
    /// ホットキーイベント引数
    /// </summary>
    public class HotkeyEventArgs : EventArgs
    {
        /// <summary>
        /// ホットキーID
        /// </summary>
        public string Id { get; }
        
        /// <summary>
        /// ホットキー定義
        /// </summary>
        public Hotkey Hotkey { get; }
        
        /// <summary>
        /// アクティブウィンドウハンドル
        /// </summary>
        public IntPtr ActiveWindowHandle { get; }
        
        /// <summary>
        /// アクティブウィンドウタイトル
        /// </summary>
        public string ActiveWindowTitle { get; }
        
        /// <summary>
        /// イベントタイムスタンプ
        /// </summary>
        public DateTime Timestamp { get; }
        
        /// <summary>
        /// 処理されたかどうか
        /// </summary>
        public bool Handled { get; set; }
        
        /// <summary>
        /// 新しいホットキーイベント引数を初期化します
        /// </summary>
        /// <param name="id">ホットキーID</param>
        /// <param name="hotkey">ホットキー定義</param>
        /// <param name="activeWindowHandle">アクティブウィンドウハンドル</param>
        /// <param name="activeWindowTitle">アクティブウィンドウタイトル</param>
        public HotkeyEventArgs(
            string id,
            Hotkey hotkey,
            IntPtr activeWindowHandle,
            string activeWindowTitle)
        {
            Id = id;
            Hotkey = hotkey;
            ActiveWindowHandle = activeWindowHandle;
            ActiveWindowTitle = activeWindowTitle;
            Timestamp = DateTime.Now;
        }
    }
    
    /// <summary>
    /// ホットキーマネージャー実装クラス
    /// </summary>
    public class HotkeyManager : IHotkeyManager
    {
        // プライベートフィールド
        private readonly Dictionary<string, RegisteredHotkey> _registeredHotkeys = new();
        private readonly IKeyboardHook _keyboardHook;
        private readonly IWindowManager _windowManager;
        private readonly ISettingsService _settingsService;
        private readonly ILogger? _logger;
        private bool _isEnabled = true;
        private bool _disposed;
        
        /// <summary>
        /// 新しいホットキーマネージャーを初期化します
        /// </summary>
        /// <param name="keyboardHook">キーボードフック</param>
        /// <param name="windowManager">ウィンドウマネージャー</param>
        /// <param name="settingsService">設定サービス</param>
        /// <param name="logger">ロガー</param>
        public HotkeyManager(
            IKeyboardHook keyboardHook,
            IWindowManager windowManager,
            ISettingsService settingsService,
            ILogger? logger = null)
        {
            _keyboardHook = keyboardHook ?? throw new ArgumentNullException(nameof(keyboardHook));
            _windowManager = windowManager ?? throw new ArgumentNullException(nameof(windowManager));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _logger = logger;
            
            // キーボードフックのイベントハンドラーを設定
            _keyboardHook.KeyDown += OnKeyDown;
            
            _logger?.LogInformation("ホットキーマネージャーが初期化されました");
        }
        
        /// <inheritdoc />
        public bool IsEnabled => _isEnabled;
        
        /// <inheritdoc />
        public IReadOnlyDictionary<string, RegisteredHotkey> RegisteredHotkeys => _registeredHotkeys;
        
        /// <inheritdoc />
        public bool RegisterHotkey(string id, Hotkey hotkey, Action<HotkeyEventArgs> action, string description)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("ホットキーIDが空です。", nameof(id));
                
            if (action == null)
                throw new ArgumentNullException(nameof(action));
                
            // 既に登録されているか確認
            if (_registeredHotkeys.ContainsKey(id))
            {
                _logger?.LogWarning("ホットキーID '{HotkeyId}'は既に登録されています。", id);
                return false;
            }
            
            // ホットキーを登録
            var registeredHotkey = new RegisteredHotkey(id, hotkey, action, description);
            _registeredHotkeys[id] = registeredHotkey;
            
            _logger?.LogInformation("ホットキー '{HotkeyId}' ({Hotkey}) が登録されました: {Description}",
                id, hotkey.GetDisplayName(), description);
                
            return true;
        }
        
        /// <inheritdoc />
        public bool UnregisterHotkey(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("ホットキーIDが空です。", nameof(id));
                
            if (!_registeredHotkeys.TryGetValue(id, out var registeredHotkey))
            {
                _logger?.LogWarning("ホットキーID '{HotkeyId}'は登録されていません。", id);
                return false;
            }
            
            // ホットキーの登録を解除
            _registeredHotkeys.Remove(id);
            
            _logger?.LogInformation("ホットキー '{HotkeyId}' ({Hotkey}) の登録が解除されました。",
                id, registeredHotkey.Hotkey.GetDisplayName());
                
            return true;
        }
        
        /// <inheritdoc />
        public bool UpdateHotkey(string id, Hotkey hotkey)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("ホットキーIDが空です。", nameof(id));
                
            if (!_registeredHotkeys.TryGetValue(id, out var registeredHotkey))
            {
                _logger?.LogWarning("ホットキーID '{HotkeyId}'は登録されていません。", id);
                return false;
            }
            
            // ホットキーを更新
            var oldHotkey = registeredHotkey.Hotkey;
            registeredHotkey.UpdateHotkey(hotkey);
            
            _logger?.LogInformation("ホットキー '{HotkeyId}' が {OldHotkey} から {NewHotkey} に更新されました。",
                id, oldHotkey.GetDisplayName(), hotkey.GetDisplayName());
                
            return true;
        }
        
        /// <inheritdoc />
        public void EnableHotkeys()
        {
            if (_isEnabled)
                return;
                
            _isEnabled = true;
            _keyboardHook.Start();
            
            _logger?.LogInformation("ホットキーが有効化されました。");
        }
        
        /// <inheritdoc />
        public void DisableHotkeys()
        {
            if (!_isEnabled)
                return;
                
            _isEnabled = false;
            _keyboardHook.Stop();
            
            _logger?.LogInformation("ホットキーが無効化されました。");
        }
        
        /// <inheritdoc />
        public bool SetHotkeyEnabled(string id, bool enabled)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("ホットキーIDが空です。", nameof(id));
                
            if (!_registeredHotkeys.TryGetValue(id, out var registeredHotkey))
            {
                _logger?.LogWarning("ホットキーID '{HotkeyId}'は登録されていません。", id);
                return false;
            }
            
            if (registeredHotkey.IsEnabled == enabled)
                return true;
                
            // ホットキーの有効/無効を設定
            registeredHotkey.IsEnabled = enabled;
            
            _logger?.LogInformation("ホットキー '{HotkeyId}' ({Hotkey}) が{State}になりました。",
                id, registeredHotkey.Hotkey.GetDisplayName(), enabled ? "有効" : "無効");
                
            return true;
        }
        
        /// <inheritdoc />
        public async Task<bool> SaveSettingsAsync()
        {
            try
            {
                // ホットキー設定を保存
                var hotkeySettings = new HotkeySettings
                {
                    IsEnabled = _isEnabled,
                    Hotkeys = _registeredHotkeys.Values
                        .Select(h => new HotkeySettingItem
                        {
                            Id = h.Id,
                            Key = h.Hotkey.Key,
                            Modifiers = h.Hotkey.Modifiers,
                            IsEnabled = h.IsEnabled,
                            Priority = h.Priority
                        })
                        .ToList()
                };
                
                await _settingsService.SaveSettingsAsync("Hotkeys", hotkeySettings);
                
                _logger?.LogInformation("ホットキー設定が保存されました。");
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "ホットキー設定の保存中にエラーが発生しました。");
                return false;
            }
        }
        
        /// <inheritdoc />
        public async Task<bool> LoadSettingsAsync()
        {
            try
            {
                // ホットキー設定を読み込み
                var hotkeySettings = await _settingsService.LoadSettingsAsync<HotkeySettings>("Hotkeys");
                if (hotkeySettings == null)
                {
                    _logger?.LogInformation("ホットキー設定が見つかりませんでした。デフォルト設定を使用します。");
                    return await ResetToDefaultsAsync();
                }
                
                // 全体の有効/無効状態を設定
                _isEnabled = hotkeySettings.IsEnabled;
                
                // 各ホットキーの設定を適用
                foreach (var item in hotkeySettings.Hotkeys)
                {
                    if (_registeredHotkeys.TryGetValue(item.Id, out var registeredHotkey))
                    {
                        var hotkey = new Hotkey(item.Key, item.Modifiers);
                        registeredHotkey.UpdateHotkey(hotkey);
                        registeredHotkey.IsEnabled = item.IsEnabled;
                        registeredHotkey.Priority = item.Priority;
                    }
                }
                
                _logger?.LogInformation("ホットキー設定が読み込まれました。");
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "ホットキー設定の読み込み中にエラーが発生しました。");
                return false;
            }
        }
        
        /// <inheritdoc />
        public async Task<bool> ResetToDefaultsAsync()
        {
            try
            {
                // デフォルトのホットキー設定
                var defaultHotkeys = new Dictionary<string, Hotkey>
                {
                    ["ToggleOverlay"] = new Hotkey(Keys.F9, ModifierKeys.Control),
                    ["StartStopCapture"] = new Hotkey(Keys.F10, ModifierKeys.Control),
                    ["ShowHideMainWindow"] = new Hotkey(Keys.F11, ModifierKeys.Control),
                    ["TranslateSelection"] = new Hotkey(Keys.T, ModifierKeys.Control | ModifierKeys.Shift)
                };
                
                // 既存のホットキーを更新
                foreach (var kvp in defaultHotkeys)
                {
                    if (_registeredHotkeys.TryGetValue(kvp.Key, out var registeredHotkey))
                    {
                        registeredHotkey.UpdateHotkey(kvp.Value);
                        registeredHotkey.IsEnabled = true;
                    }
                }
                
                _isEnabled = true;
                
                await SaveSettingsAsync();
                
                _logger?.LogInformation("ホットキー設定がデフォルトにリセットされました。");
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "ホットキー設定のリセット中にエラーが発生しました。");
                return false;
            }
        }
        
        /// <inheritdoc />
        public IReadOnlyList<RegisteredHotkey> DetectConflicts(Hotkey hotkey, string? excludeId = null)
        {
            var conflicts = new List<RegisteredHotkey>();
            
            foreach (var registeredHotkey in _registeredHotkeys.Values)
            {
                // 除外IDをスキップ
                if (excludeId != null && registeredHotkey.Id == excludeId)
                    continue;
                    
                // ホットキーが一致する場合は競合
                if (registeredHotkey.Hotkey == hotkey)
                {
                    conflicts.Add(registeredHotkey);
                }
            }
            
            return conflicts;
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
                // キーボードフックの解放
                if (_keyboardHook != null)
                {
                    _keyboardHook.KeyDown -= OnKeyDown;
                    _keyboardHook.Dispose();
                }
            }
            
            _disposed = true;
        }
        
        /// <summary>
        /// キー押下イベントハンドラー
        /// </summary>
        /// <param name="sender">送信元</param>
        /// <param name="e">イベント引数</param>
        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (!_isEnabled)
                return;
                
            // 修飾キーの状態を取得
            var modifiers = ModifierKeys.None;
            if (e.Control) modifiers |= ModifierKeys.Control;
            if (e.Alt) modifiers |= ModifierKeys.Alt;
            if (e.Shift) modifiers |= ModifierKeys.Shift;
            
            // Windowsキーの状態は別途取得する必要あり
            // 実装省略
            
            // 現在のホットキーを作成
            var currentHotkey = new Hotkey(e.KeyCode, modifiers);
            
            // アクティブウィンドウ情報を取得
            var activeWindowHandle = _windowManager.GetActiveWindowHandle();
            var activeWindowTitle = _windowManager.GetWindowTitle(activeWindowHandle);
            
            // マッチするホットキーを検索
            var matchingHotkeys = _registeredHotkeys.Values
                .Where(h => h.IsEnabled && h.Hotkey == currentHotkey)
                .OrderByDescending(h => h.Priority)
                .ToList();
                
            foreach (var hotkey in matchingHotkeys)
            {
                try
                {
                    // ホットキーイベント引数を作成
                    var args = new HotkeyEventArgs(
                        hotkey.Id,
                        hotkey.Hotkey,
                        activeWindowHandle,
                        activeWindowTitle);
                        
                    // アクションを実行
                    hotkey.Action(args);
                    
                    _logger?.LogDebug("ホットキー '{HotkeyId}' ({Hotkey}) が実行されました。",
                        hotkey.Id, hotkey.Hotkey.GetDisplayName());
                        
                    // 処理済みの場合は他のホットキーは実行しない
                    if (args.Handled)
                        break;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "ホットキー '{HotkeyId}' ({Hotkey}) の実行中にエラーが発生しました。",
                        hotkey.Id, hotkey.Hotkey.GetDisplayName());
                }
            }
        }
    }
    
    /// <summary>
    /// ホットキー設定
    /// </summary>
    public class HotkeySettings
    {
        /// <summary>
        /// ホットキーが有効かどうか
        /// </summary>
        public bool IsEnabled { get; set; } = true;
        
        /// <summary>
        /// ホットキー設定項目のリスト
        /// </summary>
        public List<HotkeySettingItem> Hotkeys { get; set; } = new();
    }
    
    /// <summary>
    /// ホットキー設定項目
    /// </summary>
    public class HotkeySettingItem
    {
        /// <summary>
        /// ホットキーID
        /// </summary>
        public string Id { get; set; } = string.Empty;
        
        /// <summary>
        /// キーコード
        /// </summary>
        public Keys Key { get; set; }
        
        /// <summary>
        /// 修飾キー
        /// </summary>
        public ModifierKeys Modifiers { get; set; }
        
        /// <summary>
        /// ホットキーが有効かどうか
        /// </summary>
        public bool IsEnabled { get; set; } = true;
        
        /// <summary>
        /// 優先度
        /// </summary>
        public int Priority { get; set; }
    }
}
```

## キーボードフックインターフェース設計案
```csharp
namespace Baketa.UI.Hotkeys
{
    /// <summary>
    /// キーボードフックインターフェース
    /// </summary>
    public interface IKeyboardHook : IDisposable
    {
        /// <summary>
        /// キー押下イベント
        /// </summary>
        event EventHandler<KeyEventArgs> KeyDown;
        
        /// <summary>
        /// キー離上イベント
        /// </summary>
        event EventHandler<KeyEventArgs> KeyUp;
        
        /// <summary>
        /// フックが有効かどうか
        /// </summary>
        bool IsActive { get; }
        
        /// <summary>
        /// フックを開始します
        /// </summary>
        void Start();
        
        /// <summary>
        /// フックを停止します
        /// </summary>
        void Stop();
    }
    
    /// <summary>
    /// Windows低レベルキーボードフック実装
    /// </summary>
    public class WindowsKeyboardHook : IKeyboardHook
    {
        // Win32 APIの定義
        // 実装省略
        
        // プライベートフィールド
        private readonly ILogger? _logger;
        private IntPtr _hookHandle = IntPtr.Zero;
        private HookProc? _hookProc;
        private bool _isActive;
        private bool _disposed;
        
        /// <summary>
        /// 新しいWindowsキーボードフックを初期化します
        /// </summary>
        /// <param name="logger">ロガー</param>
        public WindowsKeyboardHook(ILogger? logger = null)
        {
            _logger = logger;
        }
        
        /// <inheritdoc />
        public event EventHandler<KeyEventArgs>? KeyDown;
        
        /// <inheritdoc />
        public event EventHandler<KeyEventArgs>? KeyUp;
        
        /// <inheritdoc />
        public bool IsActive => _isActive;
        
        /// <inheritdoc />
        public void Start()
        {
            if (_isActive)
                return;
                
            // フック処理を設定
            _hookProc = new HookProc(HookCallback);
            
            // フックを設置
            _hookHandle = SetWindowsHookEx(
                WH_KEYBOARD_LL,
                _hookProc,
                GetModuleHandle(null),
                0);
                
            if (_hookHandle == IntPtr.Zero)
            {
                int errorCode = Marshal.GetLastWin32Error();
                _logger?.LogError("キーボードフックの設置に失敗しました。エラーコード: {ErrorCode}", errorCode);
                throw new Win32Exception(errorCode, "キーボードフックの設置に失敗しました。");
            }
            
            _isActive = true;
            _logger?.LogInformation("キーボードフックが開始されました。");
        }
        
        /// <inheritdoc />
        public void Stop()
        {
            if (!_isActive)
                return;
                
            // フックを解除
            if (_hookHandle != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookHandle);
                _hookHandle = IntPtr.Zero;
                _hookProc = null;
            }
            
            _isActive = false;
            _logger?.LogInformation("キーボードフックが停止されました。");
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
                Stop();
            }
            
            _disposed = true;
        }
        
        /// <summary>
        /// フックコールバック
        /// </summary>
        /// <param name="nCode">フックコード</param>
        /// <param name="wParam">Windowsメッセージ</param>
        /// <param name="lParam">追加情報</param>
        /// <returns>処理結果</returns>
        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                // キーボードイベントを処理
                // 実装省略
            }
            
            // 次のフックを呼び出す
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }
    }
}
```

## 実装上の注意点
- Windows APIの適切な使用と解放処理の実装
- マルチスレッドの考慮とスレッドセーフな設計
- パフォーマンス影響を最小限に抑えるためのイベント最適化
- ゲームとの競合を避けるための適切なホットキー選択
- ホットキーのカスタマイズUIの使いやすさ確保
- 設定の永続化と同期の適切な実装
- セキュリティを考慮したキーボード監視の実装

## 関連Issue/参考
- 親Issue: #10 Avalonia UIフレームワーク完全実装
- 依存Issue: #10-3 システムトレイ機能の実装
- 関連Issue: #12 設定画面
- 参照: E:\dev\Baketa\docs\3-architecture\ui-system\hotkey-system.md
- 参照: E:\dev\Baketa\docs\2-development\coding-standards\csharp-standards.md (4.2 リソース解放とDisposable)
- 参照: E:\dev\Baketa\docs\2-development\coding-standards\platform-interop.md

## マイルストーン
マイルストーン3: 翻訳とUI

## ラベル
- `type: feature`
- `priority: high`
- `component: ui`
