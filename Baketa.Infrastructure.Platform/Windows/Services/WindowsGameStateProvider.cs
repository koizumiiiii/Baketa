using System;
using System.Diagnostics;
using System.Linq;
using Baketa.Core.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Platform.Windows.Services;

/// <summary>
/// Windows固有のゲーム状態監視実装
/// Gemini改善提案: プラットフォーム固有ロジック分離
/// </summary>
public sealed class WindowsGameStateProvider : IGameStateProvider
{
    private readonly ILogger<WindowsGameStateProvider> _logger;

    // ゲーム判定用プロセス名パターン
    private static readonly string[] GameProcessPatterns =
    {
        "game", "steam", "epic", "origin", "uplay", "battle", "launcher",
        "wow", "lol", "dota", "csgo", "valorant", "apex", "fortnite",
        "minecraft", "roblox", "unity", "unreal", "genshin", "honkai"
    };

    private GameInfo? _currentGameInfo;
    private DateTime _lastCheck = DateTime.MinValue;
    private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(5); // キャッシュ間隔

    public WindowsGameStateProvider(ILogger<WindowsGameStateProvider> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// ゲーム状態変化イベント
    /// </summary>
    public event EventHandler<GameStateChangedEventArgs>? GameStateChanged;

    /// <summary>
    /// 現在のゲーム情報
    /// </summary>
    public GameInfo? CurrentGameInfo
    {
        get
        {
            UpdateGameState();
            return _currentGameInfo;
        }
    }

    /// <summary>
    /// 現在ゲームがアクティブかどうか
    /// </summary>
    public bool IsGameActive()
    {
        UpdateGameState();
        return _currentGameInfo != null;
    }

    /// <summary>
    /// ゲーム状態を更新
    /// </summary>
    private void UpdateGameState()
    {
        var now = DateTime.UtcNow;

        // キャッシュ間隔チェック（頻繁な Process.GetProcesses() を避ける）
        if (now - _lastCheck < _checkInterval)
        {
            return;
        }

        _lastCheck = now;

        try
        {
            var previousGame = _currentGameInfo;
            var detectedGame = DetectActiveGame();

            // ゲーム状態に変化があった場合のみイベント発行
            if (!GameInfoEquals(previousGame, detectedGame))
            {
                _currentGameInfo = detectedGame;

                var eventArgs = new GameStateChangedEventArgs(previousGame, detectedGame);
                GameStateChanged?.Invoke(this, eventArgs);

                if (detectedGame != null)
                {
                    _logger.LogInformation("🎮 ゲーム検出: {ProcessName} - {WindowTitle}",
                        detectedGame.ProcessName, detectedGame.WindowTitle);
                }
                else if (previousGame != null)
                {
                    _logger.LogInformation("📱 ゲーム終了: {ProcessName}", previousGame.ProcessName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ ゲーム状態更新エラー: {ErrorMessage}", ex.Message);
        }
    }

    /// <summary>
    /// アクティブなゲームを検出
    /// </summary>
    private GameInfo? DetectActiveGame()
    {
        try
        {
            // フォアグラウンドウィンドウの取得
            var foregroundWindow = GetForegroundWindow();
            if (foregroundWindow == IntPtr.Zero)
            {
                return null;
            }

            // フォアグラウンドプロセスの取得
            GetWindowThreadProcessId(foregroundWindow, out uint processId);
            var process = Process.GetProcessById((int)processId);

            if (process == null)
            {
                return null;
            }

            // ゲームプロセス判定
            var processName = process.ProcessName.ToLowerInvariant();
            var windowTitle = process.MainWindowTitle;
            var isGame = IsGameProcess(processName, windowTitle);

            if (!isGame)
            {
                return null;
            }

            // フルスクリーン判定
            var isFullScreen = IsFullScreenWindow(foregroundWindow);

            return new GameInfo(
                ProcessName: process.ProcessName,
                WindowTitle: windowTitle,
                IsFullScreen: isFullScreen,
                DetectedAt: DateTime.UtcNow
            );
        }
        catch (Exception ex)
        {
            _logger.LogTrace("ゲーム検出処理エラー: {ErrorMessage}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// プロセスがゲームかどうか判定
    /// </summary>
    private static bool IsGameProcess(string processName, string windowTitle)
    {
        // プロセス名による判定
        if (GameProcessPatterns.Any(pattern => processName.Contains(pattern)))
        {
            return true;
        }

        // ウィンドウタイトルによる判定（ゲーム特有のパターン）
        if (!string.IsNullOrEmpty(windowTitle))
        {
            var title = windowTitle.ToLowerInvariant();
            if (GameProcessPatterns.Any(pattern => title.Contains(pattern)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// フルスクリーンウィンドウかどうか判定
    /// </summary>
    private static bool IsFullScreenWindow(IntPtr window)
    {
        try
        {
            if (GetWindowRect(window, out var windowRect))
            {
                var screenWidth = GetSystemMetrics(0); // SM_CXSCREEN
                var screenHeight = GetSystemMetrics(1); // SM_CYSCREEN

                return windowRect.Left == 0 &&
                       windowRect.Top == 0 &&
                       windowRect.Right == screenWidth &&
                       windowRect.Bottom == screenHeight;
            }
        }
        catch
        {
            // Win32 API呼び出し失敗時は非フルスクリーンとして扱う
        }

        return false;
    }

    /// <summary>
    /// GameInfo比較
    /// </summary>
    private static bool GameInfoEquals(GameInfo? a, GameInfo? b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;

        return a.ProcessName == b.ProcessName &&
               a.WindowTitle == b.WindowTitle &&
               a.IsFullScreen == b.IsFullScreen;
    }

    #region Win32 API

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    #endregion
}
