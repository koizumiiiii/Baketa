using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Platform;
using Baketa.Core.Abstractions.Platform.Windows;
using Baketa.Core.Common;

namespace Baketa.Infrastructure.Platform.Adapters;

    /// <summary>
    /// Windows固有のウィンドウマネージャーをプラットフォーム非依存のウィンドウマネージャーに変換するアダプター
    /// ゲームウィンドウ検出機能とウィンドウ種類判定機能を強化しています
    /// </summary>
    public partial class WindowManagerAdapter : DisposableBase, Baketa.Core.Abstractions.Platform.IWindowManager, IWindowManagerAdapter
    {
        private readonly ILogger<WindowManagerAdapter>? _logger;
        private readonly Baketa.Core.Abstractions.Platform.Windows.IWindowManager _windowsManager;
        
        // ゲームエンジン関連の一般的なウィンドウクラス名
        private static readonly string[] _gameWindowClassPatterns = [
            "UnityWndClass",
            "GLFW30",
            "D3D",
            "SDL_app",
            "Valve",
            "CryENGINE",
            "Unreal",
            "GameMaker",
            "Godot_Engine"
        ];
        
        // ゲーム関連プロセス名パターン（正規表現）
        [GeneratedRegex(@"\b(game|play|unity|unreal|launcher|dx|steam|origin|epic|uplay)\b", RegexOptions.IgnoreCase)]
        private static partial Regex GameProcessPattern();
        private static readonly Regex _gameProcessPattern = GameProcessPattern();
        
        // 記憶するゲームウィンドウ情報（最大10個）
        private readonly Dictionary<string, IntPtr> _rememberedGameWindows = new(10);
        
        // プロセスIDとウィンドウハンドルのマッピング（キャッシュ）
        private readonly Dictionary<int, List<IntPtr>> _processWindowMap = [];
        private DateTime _lastProcessMapUpdate = DateTime.MinValue;
        
        /// <summary>
        /// WindowManagerAdapterのコンストラクタ
        /// </summary>
        /// <param name="windowsManager">Windows固有のウィンドウマネージャー</param>
        public WindowManagerAdapter(Baketa.Core.Abstractions.Platform.Windows.IWindowManager windowsManager, ILogger<WindowManagerAdapter>? logger = null)
        {
            _windowsManager = windowsManager ?? throw new ArgumentNullException(nameof(windowsManager));
            _logger = logger;
        }

        #region IWindowManager implementation
        IntPtr Baketa.Core.Abstractions.Platform.IWindowManager.GetActiveWindowHandle()
        {
            return _windowsManager.GetActiveWindowHandle();
        }

        IntPtr Baketa.Core.Abstractions.Platform.IWindowManager.FindWindowByTitle(string title)
        {
            ArgumentNullException.ThrowIfNull(title, nameof(title));
            return _windowsManager.FindWindowByTitle(title);
        }

        IntPtr Baketa.Core.Abstractions.Platform.IWindowManager.FindWindowByClass(string className)
        {
            ArgumentNullException.ThrowIfNull(className, nameof(className));
            return _windowsManager.FindWindowByClass(className);
        }

        Rectangle? Baketa.Core.Abstractions.Platform.IWindowManager.GetWindowBounds(IntPtr handle)
        {
            return _windowsManager.GetWindowBounds(handle);
        }

        Rectangle? Baketa.Core.Abstractions.Platform.IWindowManager.GetClientBounds(IntPtr handle)
        {
            return _windowsManager.GetClientBounds(handle);
        }

        string Baketa.Core.Abstractions.Platform.IWindowManager.GetWindowTitle(IntPtr handle)
        {
            return _windowsManager.GetWindowTitle(handle);
        }

        bool Baketa.Core.Abstractions.Platform.IWindowManager.IsMinimized(IntPtr handle)
        {
            return _windowsManager.IsMinimized(handle);
        }

        bool Baketa.Core.Abstractions.Platform.IWindowManager.IsMaximized(IntPtr handle)
        {
            return _windowsManager.IsMaximized(handle);
        }

        bool Baketa.Core.Abstractions.Platform.IWindowManager.SetWindowBounds(IntPtr handle, Rectangle bounds)
        {
            return _windowsManager.SetWindowBounds(handle, bounds);
        }

        bool Baketa.Core.Abstractions.Platform.IWindowManager.BringWindowToFront(IntPtr handle)
        {
            return _windowsManager.BringWindowToFront(handle);
        }

        Dictionary<IntPtr, string> Baketa.Core.Abstractions.Platform.IWindowManager.GetRunningApplicationWindows()
        {
            return _windowsManager.GetRunningApplicationWindows();
        }
        #endregion


        #region IWindowManagerAdapter implementation
        /// <summary>
        /// アクティブなウィンドウハンドルを取得
        /// </summary>
        public IntPtr GetActiveWindowHandle()
        {
            return _windowsManager.GetActiveWindowHandle();
        }

        /// <summary>
        /// 指定したタイトルを持つウィンドウハンドルを検索
        /// </summary>
        public IntPtr FindWindowByTitle(string title)
        {
            ArgumentNullException.ThrowIfNull(title, nameof(title));
            return _windowsManager.FindWindowByTitle(title);
        }

        /// <summary>
        /// 指定したクラス名を持つウィンドウハンドルを検索
        /// </summary>
        public IntPtr FindWindowByClass(string className)
        {
            ArgumentNullException.ThrowIfNull(className, nameof(className));
            return _windowsManager.FindWindowByClass(className);
        }

        /// <summary>
        /// ウィンドウの位置とサイズを取得
        /// </summary>
        public Rectangle? GetWindowBounds(IntPtr handle)
        {
            return _windowsManager.GetWindowBounds(handle);
        }

        /// <summary>
        /// ウィンドウのクライアント領域を取得
        /// </summary>
        public Rectangle? GetClientBounds(IntPtr handle)
        {
            return _windowsManager.GetClientBounds(handle);
        }

        /// <summary>
        /// ウィンドウのタイトルを取得
        /// </summary>
        public string GetWindowTitle(IntPtr handle)
        {
            return _windowsManager.GetWindowTitle(handle);
        }

        /// <summary>
        /// ウィンドウが最小化されているか確認
        /// </summary>
        public bool IsMinimized(IntPtr handle)
        {
            return _windowsManager.IsMinimized(handle);
        }

        /// <summary>
        /// ウィンドウが最大化されているか確認
        /// </summary>
        public bool IsMaximized(IntPtr handle)
        {
            return _windowsManager.IsMaximized(handle);
        }

        /// <summary>
        /// ウィンドウのサイズと位置を設定
        /// </summary>
        public bool SetWindowBounds(IntPtr handle, Rectangle bounds)
        {
            return _windowsManager.SetWindowBounds(handle, bounds);
        }

        /// <summary>
        /// ウィンドウを前面に表示
        /// </summary>
        public bool BringWindowToFront(IntPtr handle)
        {
            return _windowsManager.BringWindowToFront(handle);
        }
        #endregion
        
        /// <summary>
        /// Windows固有のウィンドウマネージャーを使用して実行中のアプリケーションのウィンドウリストを取得します
        /// </summary>
        /// <returns>ウィンドウ情報のリスト</returns>
        public IReadOnlyCollection<WindowInfo> GetRunningApplicationWindows()
        {
            // Windows固有のウィンドウリストを取得
            var rawWindows = _windowsManager.GetRunningApplicationWindows();
            
            // プロセス情報のマップを更新（必要であれば）
            UpdateProcessWindowMap();
            
            // WindowInfoオブジェクトに変換
            var windowInfos = new List<WindowInfo>(rawWindows.Count);
            
            foreach (var window in rawWindows)
            {
                var handle = window.Key;
                var title = window.Value;
                
                // プロセスIDを取得（Win32 API: GetWindowThreadProcessId）
                int processId = 0;
                
                // プロセスIDからプロセス名を取得（可能であれば）
                string processName = "";
                try
                {
                    // キャッシュからプロセスIDを検索
                    foreach (var kvp in _processWindowMap)
                    {
                        if (kvp.Value.Contains(handle))
                        {
                            processId = kvp.Key;
                            try
                            {
                                var process = Process.GetProcessById(processId);
                                processName = process.ProcessName;
                            }
                            catch (ArgumentException ex)
                            {
                                // プロセスが見つからない
                                _logger?.LogDebug(ex, "指定されたプロセスが存在しません: {ProcessId}", processId);
                            }
                            catch (InvalidOperationException ex)
                            {
                                // プロセスが既に終了している場合
                                _logger?.LogDebug(ex, "プロセスが既に終了しています: {ProcessId}", processId);
                            }
                            break;
                        }
                    }
                }
                catch (ArgumentException ex)
                {
                    // 無効な引数
                    _logger?.LogDebug(ex, "プロセスマップからプロセス情報を取得中に引数が無効でした");
                }
                catch (InvalidOperationException ex)
                {
                    // 操作が無効
                    _logger?.LogDebug(ex, "プロセス情報取得中に操作が無効でした");
                }
                
                var windowInfo = new WindowInfo
                {
                    Handle = handle,
                    Title = title,
                    Bounds = _windowsManager.GetWindowBounds(handle) ?? Rectangle.Empty,
                    ClientBounds = _windowsManager.GetClientBounds(handle) ?? Rectangle.Empty,
                    IsVisible = true, // Win32APIで取得する必要あり
                    IsMinimized = _windowsManager.IsMinimized(handle),
                    IsMaximized = _windowsManager.IsMaximized(handle),
                    WindowType = GetWindowType(handle),
                    ProcessId = processId,
                    ProcessName = processName
                };
                
                windowInfos.Add(windowInfo);
            }
            
            return windowInfos;
        }
        
        /// <summary>
        /// IWindowManager(Windows)からIWindowManager(Core)への適応を行います
        /// </summary>
        /// <param name="windowsManager">Windows固有のウィンドウマネージャー</param>
        /// <returns>プラットフォーム非依存のウィンドウマネージャー</returns>
        public Baketa.Core.Abstractions.Platform.IWindowManager AdaptWindowManager(Baketa.Core.Abstractions.Platform.Windows.IWindowManager windowsManager)
        {
            ArgumentNullException.ThrowIfNull(windowsManager, nameof(windowsManager));
            return new WindowManagerAdapter(windowsManager);
        }
        
        /// <summary>
        /// ゲームウィンドウを特定します
        /// 多段階アプローチによるゲームウィンドウ検出アルゴリズムを実装しています
        /// </summary>
        /// <param name="gameTitle">ゲームタイトル（部分一致）</param>
        /// <returns>ゲームウィンドウのハンドル。見つからなければIntPtr.Zero</returns>
        public IntPtr FindGameWindow(string gameTitle)
        {
            ArgumentNullException.ThrowIfNull(gameTitle, nameof(gameTitle));
            
            // 1. 過去に記憶したゲームウィンドウを確認
            if (_rememberedGameWindows.TryGetValue(gameTitle, out var rememberedHandle))
            {
                // 記憶されたウィンドウが引き続き存在するか確認
                try
                {
                    var title = _windowsManager.GetWindowTitle(rememberedHandle);
                    if (!string.IsNullOrEmpty(title))
                    {
                        return rememberedHandle;
                    }
                }
                catch (InvalidOperationException ex)
                {
                    // 無効なウィンドウハンドル
                    _logger?.LogWarning(ex, "記憶されたゲームウィンドウのタイトル取得に失敗しました: {GameTitle}", gameTitle);
                }
                catch (ArgumentException ex)
                {
                    // 引数が無効
                    _logger?.LogWarning(ex, "記憶されたゲームウィンドウのタイトル取得時に引数エラーが発生しました: {GameTitle}", gameTitle);
                }
                // 存在しない場合は記憶から削除
                _rememberedGameWindows.Remove(gameTitle);
            }
            
            // ウィンドウリストを取得
            var windows = _windowsManager.GetRunningApplicationWindows();
            
            // 2. タイトルベースの特定（最も直接的）
            foreach (var window in windows)
            {
                if (window.Value?.Contains(gameTitle, StringComparison.OrdinalIgnoreCase) == true)
                {
                    // タイトルが一致するウィンドウを見つけた
                    var handle = window.Key;
                    
                    // ウィンドウの種類を確認
                    var windowType = GetWindowType(handle);
                    if (windowType == WindowType.Game)
                    {
                        // ゲームとしてマッチしたので記憶して返す
                        _rememberedGameWindows[gameTitle] = handle;
                        return handle;
                    }
                }
            }
            
            // 3. アクティブウィンドウを確認（現在プレイ中のゲームである可能性）
            var activeHandle = _windowsManager.GetActiveWindowHandle();
            if (activeHandle != IntPtr.Zero)
            {
                var windowType = GetWindowType(activeHandle);
                if (windowType == WindowType.Game)
                {
                    // アクティブウィンドウがゲームと判断された場合
                    _rememberedGameWindows[gameTitle] = activeHandle;
                    return activeHandle;
                }
            }
            
            // 4. ヒューリスティック評価によるゲームウィンドウ検索
            var candidateWindows = new List<(IntPtr handle, int score)>();
            
            // プロセス情報のマップを更新（必要であれば）
            UpdateProcessWindowMap();
            
            foreach (var window in windows)
            {
                var handle = window.Key;
                var title = window.Value;
                
                // 各ウィンドウのスコアを計算
                int score = 0;
                
                // ウィンドウの種類を確認
                var windowType = GetWindowType(handle);
                if (windowType == WindowType.Game)
                {
                    score += 30; // ゲームウィンドウの特徴を持つ
                }
                
                // ウィンドウサイズを考慮（ゲームは通常大きなウィンドウまたは全画面）
                var bounds = _windowsManager.GetWindowBounds(handle);
                if (bounds.HasValue)
                {
                    if (bounds.Value.Width >= 800 && bounds.Value.Height >= 600)
                    {
                        score += 10; // 十分なサイズがある
                    }
                    
                    // アスペクト比をチェック（一般的なゲーム比率に近いか）
                    float ratio = (float)bounds.Value.Width / bounds.Value.Height;
                    if (ratio >= 1.3f && ratio <= 1.9f) // 一般的なゲームアスペクト比
                    {
                        score += 5;
                    }
                }
                
                // プロセス名が何らかのゲーム関連と思われる
                int processId = 0;
                foreach (var kvp in _processWindowMap)
                {
                    if (kvp.Value.Contains(handle))
                    {
                        processId = kvp.Key;
                        break;
                    }
                }
                
                if (processId != 0)
                {
                    try
                    {
                    var process = Process.GetProcessById(processId);
                    if (_gameProcessPattern.IsMatch(process.ProcessName))
                    {
                    score += 20; // プロセス名がゲーム関連
                    }
                    }
                    catch (ArgumentException ex)
                    {
                    // プロセスが存在しない
                    _logger?.LogDebug(ex, "指定されたプロセスIDが存在しません: {ProcessId}", processId);
                    }
                    catch (InvalidOperationException ex)
                    {
                    // プロセスへのアクセス権がない
                _logger?.LogDebug(ex, "プロセスへのアクセス権がありません: {ProcessId}", processId);
            }
                }
                
                candidateWindows.Add((handle, score));
            }
            
            // スコアの高い順にソート
            candidateWindows.Sort((a, b) => b.score.CompareTo(a.score));
            
            // 最もスコアの高いウィンドウを返す（十分なスコアがある場合）
            if (candidateWindows.Count > 0 && candidateWindows[0].score >= 20)
            {
                var bestCandidate = candidateWindows[0].handle;
                _rememberedGameWindows[gameTitle] = bestCandidate;
                return bestCandidate;
            }
            
            // 適切なウィンドウが見つからなかった
            return IntPtr.Zero;
        }
        
        /// <summary>
        /// ウィンドウの種類を判定します
        /// ウィンドウスタイル、クラス名、サイズなどに基づいて種類を判定します
        /// </summary>
        /// <param name="handle">ウィンドウハンドル</param>
        /// <returns>ウィンドウの種類</returns>
        public WindowType GetWindowType(IntPtr handle)
        {
            // 実際の判定は保護メソッドに委託
            return GetWindowTypeInternal(handle);
        }
        
        /// <summary>
        /// ウィンドウの種類を判定する内部メソッド
        /// オーバーライド可能なためテスト時にモック可能
        /// </summary>
        /// <param name="handle">ウィンドウハンドル</param>
        /// <returns>ウィンドウの種類</returns>
        internal protected virtual WindowType GetWindowTypeInternal(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
            {
                return WindowType.Unknown;
            }
            
            // ウィンドウクラス名を取得（Win32 APIラッパーが必要）
            string className = GetWindowClassName(handle);
            
            // ゲームエンジン関連のクラス名かどうかをチェック
            foreach (var pattern in _gameWindowClassPatterns)
            {
                if (!string.IsNullOrEmpty(className) && className.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    return WindowType.Game;
                }
            }
            
            // ウィンドウスタイルをチェック
            long windowStyle = GetWindowStyle(handle);
            
            // ダイアログのスタイルを持つかどうか
            if ((windowStyle & 0x00C00000L) == 0 && // WS_CAPTION が無い
                (windowStyle & 0x00800000L) != 0)   // WS_BORDER がある
            {
                return WindowType.Dialog;
            }
            
            // ツールウィンドウのスタイルを持つかどうか
            if ((windowStyle & 0x00000080L) != 0)   // WS_EX_TOOLWINDOW
            {
                return WindowType.Tool;
            }
            
            // システムウィンドウのチェック
            var title = _windowsManager.GetWindowTitle(handle);
            if (string.IsNullOrEmpty(title) || 
                (className != null && (className.StartsWith("Progman", StringComparison.Ordinal) || 
                className.StartsWith("Shell_", StringComparison.Ordinal) ||
                className.StartsWith("DV2ControlHost", StringComparison.Ordinal))))
            {
                return WindowType.System;
            }
            
            // サイズの特徴でゲームっぽさを判定
            var bounds = _windowsManager.GetWindowBounds(handle);
            if (bounds != null && bounds.HasValue)
            {
                // 全画面に近いサイズかどうか
                var primaryScreen = System.Windows.Forms.Screen.PrimaryScreen;
                if (primaryScreen != null)
                {
                    var screenWidth = primaryScreen.Bounds.Width;
                    var screenHeight = primaryScreen.Bounds.Height;
                    
                    var widthRatio = (double)bounds.Value.Width / screenWidth;
                    var heightRatio = (double)bounds.Value.Height / screenHeight;
                    
                    if (widthRatio > 0.9 && heightRatio > 0.9 && 
                        className != "ApplicationFrameWindow" && // 標準的なWindows 10アプリを除外
                        !string.IsNullOrEmpty(title) && !title.Contains("Microsoft", StringComparison.OrdinalIgnoreCase)) // Microsoftアプリを除外
                    {
                        return WindowType.Game; // 全画面に近いサイズはゲームの可能性が高い
                    }
                }
            }
            
            // プロセス情報に基づく判定
            int processId = 0;
            foreach (var kvp in _processWindowMap)
            {
                if (kvp.Value.Contains(handle))
                {
                    processId = kvp.Key;
                    break;
                }
            }
            
            if (processId != 0)
            {
            try
            {
            var process = Process.GetProcessById(processId);
            if (_gameProcessPattern.IsMatch(process.ProcessName))
            {
            return WindowType.Game; // プロセス名がゲーム関連
            }
            }
            catch (ArgumentException ex)
            {
                // プロセスIDが無効
                _logger?.LogDebug(ex, "ウィンドウ種別判定で無効なプロセスID: {ProcessId}", processId);
            }
            catch (InvalidOperationException ex)
            {
            // プロセスが既に終了している場合など
                _logger?.LogDebug(ex, "ウィンドウ種別判定でプロセスへのアクセスが失敗しました: {ProcessId}", processId);
            }
                }
            
            // デフォルトは通常のアプリケーションウィンドウ
            return WindowType.Normal;
        }
        
        /// <inheritdoc />
        public string? GetWindowThumbnail(IntPtr handle, int maxWidth = 160, int maxHeight = 120)
        {
            return _windowsManager.GetWindowThumbnail(handle, maxWidth, maxHeight);
        }
        
        /// <summary>
        /// ウィンドウクラス名を取得
        /// </summary>
        /// <param name="_">ウィンドウハンドル</param>
        /// <returns>ウィンドウクラス名</returns>
        private string GetWindowClassName(IntPtr handle)
        {
            try
            {
                // Win32 APIの呼び出しが必要だが、現在の設計では直接実装できないため、
                // ラッパークラスやユーティリティを使用する必要がある
                // TODO: Win32 API呼び出しの実装を追加する
                // 現時点ではダミー実装として空文字列を返す
                return "";
            }
            catch (Win32Exception ex)
            {
                _logger?.LogError(ex, "ウィンドウクラス名の取得に失敗しました: {HandleValue}", handle);
                return "";
            }
            catch (ArgumentException ex)
            {
                _logger?.LogError(ex, "ウィンドウクラス名取得時に引数エラーが発生しました: {HandleValue}", handle);
                return "";
            }
            catch (InvalidOperationException ex)
            {
                _logger?.LogError(ex, "ウィンドウクラス名取得時に操作が無効です: {HandleValue}", handle);
                return "";
            }
        }
        
        /// <summary>
        /// ウィンドウスタイルを取得
        /// </summary>
        /// <param name="_">ウィンドウハンドル</param>
        /// <returns>ウィンドウスタイル</returns>
        private long GetWindowStyle(IntPtr handle)
        {
            try
            {
                // Win32 APIの呼び出しが必要だが、現在の設計では直接実装できないため、
                // ラッパークラスやユーティリティを使用する必要がある
                // TODO: Win32 API呼び出しの実装を追加する
                // 現時点ではダミー実装として0を返す
                return 0;
            }
            catch (Win32Exception ex)
            {
                _logger?.LogError(ex, "ウィンドウスタイルの取得に失敗しました: {HandleValue}", handle);
                return 0;
            }
            catch (ArgumentException ex)
            {
                _logger?.LogError(ex, "ウィンドウスタイル取得時に引数エラーが発生しました: {HandleValue}", handle);
                return 0;
            }
            catch (InvalidOperationException ex)
            {
                _logger?.LogError(ex, "ウィンドウスタイル取得時に操作が無効です: {HandleValue}", handle);
                return 0;
            }
        }
        
        /// <summary>
        /// プロセスとウィンドウのマッピング情報を更新
        /// </summary>
        private void UpdateProcessWindowMap()
        {
            // 一定時間間隔でのみ更新（パフォーマンス最適化）
            if ((DateTime.UtcNow - _lastProcessMapUpdate).TotalSeconds < 30 && _processWindowMap.Count > 0)
            {
                return;
            }
            
            _processWindowMap.Clear();
            
            try
            {
                // 🚀 UltraThink緊急修正: Process.GetProcesses()によるメモリ競合を完全回避
                // ネイティブキャプチャとの同時実行で System.AccessViolationException が発生
                // 軽量実装により安全性を優先
                
                _logger?.LogDebug("🔧 WindowManagerAdapter: Process.GetProcesses()スキップ - メモリ競合回避のため");
                
                // プロセスマップは空のまま維持（機能は制限されるが安全性を優先）
                // var processes = Process.GetProcesses(); // <- 無効化
                var processes = Array.Empty<Process>(); // 空配列で安全な実装
                
                foreach (var process in processes)
                {
                    try
                    {
                        // プロセスのメインウィンドウハンドルを取得
                        if (process.MainWindowHandle != IntPtr.Zero)
                        {
                            if (!_processWindowMap.TryGetValue(process.Id, out var windowHandles))
                            {
                                windowHandles = [];
                                _processWindowMap[process.Id] = windowHandles;
                            }
                            windowHandles.Add(process.MainWindowHandle);
                        }
                        
                        // プロセスに関連する他のウィンドウハンドルを取得する必要がある場合は
                        // Win32 APIの EnumWindows と GetWindowThreadProcessId を使用
                        // この部分は現時点では実装しない
                    }
                    catch (Win32Exception ex)
                    {
                        // Win32 API関連のエラー
                        _logger?.LogDebug(ex, "プロセスのウィンドウ情報取得でWin32エラーが発生しました: {ProcessId}", process.Id);
                    }
                    catch (InvalidOperationException ex)
                    {
                        // プロセスが終了した可能性
                        _logger?.LogDebug(ex, "プロセス情報取得中にプロセスが終了した可能性があります: {ProcessId}", process.Id);
                    }
                    catch (IOException ex)
                    {
                        try
                        {
                            // ファイル操作関連のエラー
                            if (process != null)
                            {
                                _logger?.LogDebug(ex, "プロセスのウィンドウ情報取得中にIOエラーが発生しました: {ProcessId}", process.Id);
                            }
                            else
                            {
                                _logger?.LogDebug(ex, "プロセスのウィンドウ情報取得中にIOエラーが発生しました");
                            }
                        }
                        catch (NullReferenceException nullEx)
                        {
                            // process.Idがアクセスできない場合
                            _logger?.LogDebug(nullEx, "プロセスアクセス中にNull参照が発生しました");
                        }
                    }
                    catch (OutOfMemoryException ex)
                    {
                        try
                        {
                            // メモリ不足
                            if (process != null)
                            {
                                _logger?.LogDebug(ex, "プロセスのウィンドウ情報取得中にメモリ不足が発生しました: {ProcessId}", process.Id);
                            }
                            else
                            {
                                _logger?.LogDebug(ex, "プロセスのウィンドウ情報取得中にメモリ不足が発生しました");
                            }
                        }
                        catch (NullReferenceException nullEx)
                        {
                            // process.Idがアクセスできない場合
                            _logger?.LogDebug(nullEx, "プロセスアクセス中にNull参照が発生しました");
                        }
                    }
                    catch (TimeoutException ex)
                    {
                        try
                        {
                            // タイムアウト
                            if (process != null)
                            {
                                _logger?.LogDebug(ex, "プロセスのウィンドウ情報取得中にタイムアウトが発生しました: {ProcessId}", process.Id);
                            }
                            else
                            {
                                _logger?.LogDebug(ex, "プロセスのウィンドウ情報取得中にタイムアウトが発生しました");
                            }
                        }
                        catch (NullReferenceException nullEx)
                        {
                            // process.Idがアクセスできない場合
                            _logger?.LogDebug(nullEx, "プロセスアクセス中にNull参照が発生しました");
                        }
                    }
                    finally
                    {
                        // プロセスオブジェクトを解放
                        process.Dispose();
                    }
                }
            }
            catch (InvalidOperationException ex)
            {
                // プロセス一覧取得の操作が無効
                _logger?.LogError(ex, "プロセス一覧の取得操作が無効です");
            }
            catch (UnauthorizedAccessException ex)
            {
                // アクセス権限がない
                _logger?.LogError(ex, "プロセス一覧の取得に必要な権限がありません");
            }
            catch (System.Security.SecurityException ex)
            {
                // セキュリティ例外
                _logger?.LogError(ex, "プロセス一覧の取得でセキュリティ例外が発生しました");
            }
            
            _lastProcessMapUpdate = DateTime.UtcNow;
        }

        /// <summary>
        /// マネージドリソースを解放します
        /// </summary>
        protected override void DisposeManagedResources()
        {
            // Windowsウィンドウマネージャーを解放
            if (_windowsManager is IDisposable windowsManagerDisposable)
            {
                windowsManagerDisposable.Dispose();
            }
            
            // 記憶したゲームウィンドウ情報をクリア
            _rememberedGameWindows.Clear();
            _processWindowMap.Clear();
        }
    }
