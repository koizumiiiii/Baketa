using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Platform.Windows.Adapters;
using Baketa.Core.Utilities;
using Baketa.UI.Framework.Events;
using Baketa.UI.Framework;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using WindowInfo = Baketa.Core.Abstractions.Platform.Windows.Adapters.WindowInfo;

namespace Baketa.UI.ViewModels;

/// <summary>
/// ウィンドウ選択ダイアログのViewModel
/// </summary>
public class WindowSelectionDialogViewModel : ViewModelBase
{
    private readonly IWindowManagerAdapter _windowManager;
    private WindowInfo? _selectedWindow;
    private bool _isLoading;

    public WindowSelectionDialogViewModel(
        IEventAggregator eventAggregator,
        ILogger<WindowSelectionDialogViewModel> logger,
        IWindowManagerAdapter windowManager)
        : base(eventAggregator, logger)
    {
        _windowManager = windowManager ?? throw new ArgumentNullException(nameof(windowManager));
        
        AvailableWindows = [];
        
        // コマンドの初期化（UI スレッドで安全に初期化）
        try
        {
            SelectWindowCommand = ReactiveCommand.CreateFromTask<WindowInfo>(ExecuteSelectWindowAsync,
                outputScheduler: RxApp.MainThreadScheduler);
            CancelCommand = ReactiveCommand.Create(ExecuteCancel,
                outputScheduler: RxApp.MainThreadScheduler);
            RefreshCommand = ReactiveCommand.CreateFromTask(ExecuteRefreshAsync,
                outputScheduler: RxApp.MainThreadScheduler);
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "WindowSelectionDialogViewModelのReactiveCommand初期化エラー");
            throw;
        }
        
        // 🚀 GEMINI FIX: レースコンディション回避 - コンストラクタ内非同期初期化を削除
        // Task.Runによる初期ロードは削除し、明示的なRefreshCommand呼び出しに変更
        
        Logger?.LogInformation("WindowSelectionDialogViewModel初期化完了 - 非同期ロードはRefreshCommand経由で実行");
    }

    /// <summary>
    /// 利用可能なウィンドウリスト
    /// </summary>
    public ObservableCollection<WindowInfo> AvailableWindows { get; }

    /// <summary>
    /// 選択中のウィンドウ
    /// </summary>
    public WindowInfo? SelectedWindow
    {
        get => _selectedWindow;
        set
        {
            try
            {
                this.RaiseAndSetIfChanged(ref _selectedWindow, value);
                // CanSelectプロパティの変更通知
                this.RaisePropertyChanged(nameof(CanSelect));
            }
            catch (InvalidOperationException ex)
            {
                Logger?.LogWarning(ex, "UIスレッド違反でSelectedWindow設定失敗 - 直接設定で続行");
                _selectedWindow = value;
                // 直接設定でもCanSelectの通知を試行
                try
                {
                    this.RaisePropertyChanged(nameof(CanSelect));
                }
                catch { /* セカンダリ例外は無視 */ }
            }
        }
    }

    /// <summary>
    /// ロード中状態
    /// </summary>
    public new bool IsLoading
    {
        get => _isLoading;
        private set
        {
            try
            {
                this.RaiseAndSetIfChanged(ref _isLoading, value);
            }
            catch (InvalidOperationException ex)
            {
                Logger?.LogWarning(ex, "UIスレッド違反でIsLoading設定失敗 - 直接設定で続行");
                _isLoading = value;
            }
        }
    }

    /// <summary>
    /// 選択可能状態（ウィンドウが選択されている）
    /// </summary>
    public bool CanSelect => SelectedWindow != null;

    /// <summary>
    /// ウィンドウ選択コマンド
    /// </summary>
    public ReactiveCommand<WindowInfo, Unit> SelectWindowCommand { get; }

    /// <summary>
    /// キャンセルコマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    /// <summary>
    /// 更新コマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }

    /// <summary>
    /// ダイアログ結果
    /// </summary>
    public WindowInfo? DialogResult { get; internal set; }

    /// <summary>
    /// ダイアログが閉じられたかどうか
    /// </summary>
    private bool _isClosed;
    public bool IsClosed
    {
        get => _isClosed;
        private set
        {
            try
            {
                this.RaiseAndSetIfChanged(ref _isClosed, value);
            }
            catch (InvalidOperationException ex)
            {
                Logger?.LogWarning(ex, "UIスレッド違反でIsClosed設定失敗 - 直接設定で続行");
                _isClosed = value;
            }
        }
    }

    /// <summary>
    /// ユーザー操作可能なウィンドウかを判定（翻訳対象として有効）
    /// 方針: 86個→10-20個程度に大幅削減してパフォーマンス向上
    /// </summary>
    /// <param name="window">ウィンドウ情報</param>
    /// <returns>有効な場合はtrue</returns>
    private static bool IsValidWindow(WindowInfo window)
    {
        // 基本的な条件チェック
        if (string.IsNullOrWhiteSpace(window.Title))
        {
            Console.WriteLine($"🔍 IsValidWindow: SKIP - 空タイトル: Handle={window.Handle}");
            return false;
        }

        // Baketa自身を除外（重要）
        if (window.Title.Contains("Baketa", StringComparison.OrdinalIgnoreCase) ||
            window.Title.Contains("WindowSelectionDialog", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"🔍 IsValidWindow: SKIP - Baketa自身: '{window.Title}'");
            return false;
        }

        // 表示状態のチェック (最小化ウィンドウは含める)
        if (!window.IsVisible)
        {
            Console.WriteLine($"🔍 IsValidWindow: SKIP - 非表示: '{window.Title}' Handle={window.Handle}");
            return false;
        }

        // ウィンドウサイズの有効性チェック
        if (window.Bounds.Width <= 0 || window.Bounds.Height <= 0)
        {
            Console.WriteLine($"🔍 IsValidWindow: SKIP - 無効サイズ: '{window.Title}' Size={window.Bounds.Width}x{window.Bounds.Height}");
            return false;
        }

        // 翻訳意味のあるサイズを厳格化（ユーザー操作ウィンドウ想定）
        if (window.Bounds.Width < 200 || window.Bounds.Height < 100)
        {
            Console.WriteLine($"🔍 IsValidWindow: SKIP - 翻訳対象外サイズ: '{window.Title}' Size={window.Bounds.Width}x{window.Bounds.Height}");
            return false;
        }

        // システムウィンドウ系の除外（大幅拡張）
        if (IsSystemOrBackgroundWindow(window.Title))
        {
            Console.WriteLine($"🔍 IsValidWindow: SKIP - システム/バックグラウンドウィンドウ: '{window.Title}'");
            return false;
        }

        // 開発ツール・デバッガーの除外
        if (IsDeveloperToolWindow(window.Title))
        {
            Console.WriteLine($"🔍 IsValidWindow: SKIP - 開発ツール: '{window.Title}'");
            return false;
        }

        Console.WriteLine($"🔍 IsValidWindow: ✅ VALID - '{window.Title}' Size={window.Bounds.Width}x{window.Bounds.Height} Minimized={window.IsMinimized}");
        return true;
    }

    /// <summary>
    /// システムウィンドウ・バックグラウンドプロセスウィンドウかどうかを判定（大幅拡張）
    /// </summary>
    /// <param name="title">ウィンドウタイトル</param>
    /// <returns>システム/バックグラウンドウィンドウの場合はtrue</returns>
    private static bool IsSystemOrBackgroundWindow(string title)
    {
        // 🔥 UltraThink修正: 翻訳対象アプリを誤って除外しないよう条件を厳格化
        var systemPatterns = new[]
        {
            // Windows システムコア（完全一致または明確な識別子）
            "Program Manager", "Desktop Window Manager", "Windows Shell Experience Host",
            "Microsoft Text Input Application", "Windows Security", "Windows Defender",

            // システム管理ツール（完全一致）
            "Task Manager", "Settings", "Control Panel", "Registry Editor", "Event Viewer",
            "Device Manager", "Computer Management", "Disk Cleanup", "System Configuration",

            // Windows サービス・プロセス（具体的なサービス名）
            "Windows Audio Device Graph Isolation", "Windows Audio", 
            "Antimalware Service Executable", "Windows Security Health Service",

            // システムトレイ・通知系（完全一致）
            "Action Center", "Notification Center", "System Tray", "Hidden Icon",

            // Windowsストア・更新系（完全一致）
            "Microsoft Store", "Windows Update", "Software Distribution Service",

            // サービスホストプロセス（具体的な名前）
            "Service Host: Local System", "Background Task Host",

            // スクリーンセーバー・ロック画面（完全一致）
            "Logon UI Host", "Lock Screen", "Screen Saver",

            // 空白・無効なタイトル
            "", " ", "\t", "\n"
        };

        var backgroundPatterns = new[]
        {
            // 🔥 UltraThink修正: ゲームランチャーはバックグラウンド時のみ除外
            "Steam Client Bootstrapper", "Epic Games Launcher (minimized)", 
            "Battle.net (background)", "Origin (minimized)", "Uplay (background)",

            // バックグラウンド同期・クラウド（明確にバックグラウンド状態）
            "OneDrive - ", "Google Drive (syncing)", "Dropbox (syncing)", 
            "iCloud (background)", "Backup in progress",

            // 音楽・メディアプレイヤー（明確に一時停止・非アクティブ）
            "Spotify (paused)", "iTunes Helper", "VLC Media Player (stopped)",

            // チャット・通信アプリ（明確に最小化状態）
            "Discord (minimized)", "Skype (background)", "Teams (background)",

            // 自動更新・アップデーター（具体的なアップデーター）
            "Adobe Updater", "Chrome Update", "Firefox Update Service",

            // 印刷・スプール（具体的なプリンター関連）
            "Print Spooler Service", "Printer Queue Manager"
        };

        // 🔥 UltraThink修正: 完全一致またはより具体的なパターンマッチに変更
        return systemPatterns.Any(pattern => 
                   string.Equals(title, pattern, StringComparison.OrdinalIgnoreCase) ||
                   (pattern.Contains("Service") && title.Equals(pattern, StringComparison.OrdinalIgnoreCase))
               ) ||
               backgroundPatterns.Any(pattern => 
                   title.Contains(pattern, StringComparison.OrdinalIgnoreCase) ||
                   (pattern.Contains("(") && title.EndsWith(pattern.Substring(pattern.IndexOf('(')), StringComparison.OrdinalIgnoreCase))
               );
    }

    /// <summary>
    /// 開発ツール・デバッガーウィンドウかどうかを判定
    /// </summary>
    /// <param name="title">ウィンドウタイトル</param>
    /// <returns>開発ツールの場合はtrue</returns>
    private static bool IsDeveloperToolWindow(string title)
    {
        var developerPatterns = new[]
        {
            // IDEs
            "Visual Studio", "JetBrains", "IntelliJ", "PyCharm", "WebStorm", "ReSharper",
            "Code", "Atom", "Sublime Text", "Notepad++", "Vim", "Emacs",

            // デバッガー・プロファイラー
            "Debugger", "Debug", "Profiler", "Performance", "Memory Dump",
            "JetBrains dotMemory", "JetBrains dotTrace", "PerfView",

            // ターミナル・コマンドライン
            "Command Prompt", "PowerShell", "Windows Terminal", "Git Bash",
            "cmd.exe", "powershell.exe", "bash", "zsh", "Terminal",

            // データベース管理
            "SQL Server Management Studio", "MySQL Workbench", "pgAdmin",
            "MongoDB Compass", "Redis Desktop Manager",

            // API・ネットワークツール
            "Postman", "Insomnia", "Fiddler", "Wireshark", "Charles Proxy",

            // 設計・モデリング
            "Draw.io", "Lucidchart", "Visio", "Enterprise Architect"
        };

        return developerPatterns.Any(pattern => title.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 利用可能なウィンドウを読み込み（タイムアウト機能付き・高速化版）
    /// </summary>
    private async Task LoadAvailableWindowsAsync()
    {
        const int TimeoutMs = 30000; // 30秒タイムアウト
        var startTime = DateTime.UtcNow;

        try
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsLoading = true;
            });

            Logger?.LogDebug("🔍 LoadAvailableWindowsAsync開始 - Timeout: {TimeoutMs}ms", TimeoutMs);
            Console.WriteLine($"🔍 LoadAvailableWindowsAsync開始 - Timeout: {TimeoutMs}ms");

            // タイムアウト機能付きバックグラウンド処理
            using var cts = new CancellationTokenSource(TimeoutMs);

            var windows = await Task.Run(async () =>
            {
                var operationStart = DateTime.UtcNow;
                Logger?.LogDebug("🔍 _windowManager.GetRunningApplicationWindows()呼び出し開始");
                Console.WriteLine("🔍 _windowManager.GetRunningApplicationWindows()呼び出し開始");

                // ウィンドウ取得をタイムアウトで保護
                var rawWindows = await Task.Run(() =>
                {
                    cts.Token.ThrowIfCancellationRequested();
                    return _windowManager.GetRunningApplicationWindows();
                });

                var windowsGetTime = (DateTime.UtcNow - operationStart).TotalMilliseconds;
                Logger?.LogDebug("🔍 _windowManager.GetRunningApplicationWindows()完了: {Count}個 ({ElapsedMs}ms)", rawWindows.Count, windowsGetTime);
                Console.WriteLine($"🔍 _windowManager.GetRunningApplicationWindows()完了: {rawWindows.Count}個 ({windowsGetTime:F1}ms)");

                // 高速化: バッチ処理でUIを段階的に更新
                var filteredWindows = new List<WindowInfo>();
                var batchSize = 20; // 20個ずつ処理
                int skippedCount = 0;
                int processedCount = 0;

                for (int i = 0; i < rawWindows.Count; i += batchSize)
                {
                    cts.Token.ThrowIfCancellationRequested();

                    var batch = rawWindows.Skip(i).Take(batchSize);
                    var batchResults = new List<WindowInfo>();

                    foreach (var window in batch)
                    {
                        try
                        {
                            processedCount++;

                            // キャンセルチェック
                            if (processedCount % 10 == 0)
                            {
                                cts.Token.ThrowIfCancellationRequested();
                                var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                                Logger?.LogDebug("🔄 処理進捗: {ProcessedCount}/{TotalCount} ({ElapsedMs}ms)", processedCount, rawWindows.Count, elapsed);
                                Console.WriteLine($"🔄 処理進捗: {processedCount}/{rawWindows.Count} ({elapsed:F0}ms)");
                            }

                            // IsValidWindowの判定と自己除外を安全に実行
                            if (IsValidWindow(window) && window.Title != "WindowSelectionDialog")
                            {
                                batchResults.Add(window);

                                // 重要なウィンドウは個別ログ
                                if (window.Title.Contains("Discord") || window.Title.Contains("Chrome") || window.Title.Contains("Game"))
                                {
                                    Logger?.LogDebug("✅ 重要ウィンドウ追加: Handle={Handle}, Title='{Title}'", window.Handle, window.Title);
                                    Console.WriteLine($"✅ 重要ウィンドウ追加: Handle={window.Handle}, Title='{window.Title}'");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            skippedCount++;
                            // 問題のウィンドウをログに出力し、処理を続行
                            Logger?.LogWarning(ex, "[WINDOW_ERROR] Processing error for Handle: {Handle}, Title: {Title}",
                                window.Handle, window.Title ?? "N/A");
                            Console.WriteLine($"[WINDOW_ERROR] Skipping window Handle={window.Handle}, Title='{window.Title ?? "N/A"}' due to: {ex.Message}");
                        }
                    }

                    // バッチ結果をメインリストに追加
                    filteredWindows.AddRange(batchResults);

                    // バッチ完了時にUIを部分更新（非同期）
                    if (batchResults.Count > 0 && i + batchSize < rawWindows.Count)
                    {
                        _ = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            if (i == 0) AvailableWindows.Clear(); // 初回のみクリア
                            foreach (var window in batchResults)
                            {
                                AvailableWindows.Add(window);
                            }
                        });

                        Logger?.LogDebug("🔄 バッチUI更新: {BatchCount}個追加 (総計{TotalCount}個)", batchResults.Count, filteredWindows.Count);
                        Console.WriteLine($"🔄 バッチUI更新: {batchResults.Count}個追加 (総計{filteredWindows.Count}個)");
                    }
                }

                if (skippedCount > 0)
                {
                    Logger?.LogWarning("[WINDOW_SKIP] Skipped {Count} problematic windows", skippedCount);
                    Console.WriteLine($"[WINDOW_SKIP] Skipped {skippedCount} problematic windows");
                }

                var totalElapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                Logger?.LogDebug("🔍 フィルタリング完了: {Count}個の有効ウィンドウ ({ElapsedMs}ms)", filteredWindows.Count, totalElapsed);
                Console.WriteLine($"🔍 フィルタリング完了: {filteredWindows.Count}個の有効ウィンドウ ({totalElapsed:F1}ms)");

                return filteredWindows;
            }, cts.Token).ConfigureAwait(false);

            var finalElapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
            Logger?.LogDebug("🔄 最終UI更新開始: {Count}個のウィンドウ ({ElapsedMs}ms)", windows.Count, finalElapsed);
            Console.WriteLine($"🔄 最終UI更新開始: {windows.Count}個のウィンドウ ({finalElapsed:F1}ms)");

            // 最終UI更新
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                AvailableWindows.Clear();
                foreach (var window in windows)
                {
                    AvailableWindows.Add(window);
                }

                Logger?.LogDebug("🔄 AvailableWindows.Count最終更新: {Count}", AvailableWindows.Count);
                Console.WriteLine($"🔄 AvailableWindows.Count最終更新: {AvailableWindows.Count}");
            });

            var completeElapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
            Logger?.LogDebug("✅ LoadAvailableWindowsAsync完了: {Count}個 ({ElapsedMs}ms)", AvailableWindows.Count, completeElapsed);
            Console.WriteLine($"✅ LoadAvailableWindowsAsync完了: {AvailableWindows.Count}個 ({completeElapsed:F1}ms)");
        }
        catch (OperationCanceledException)
        {
            var timeoutElapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
            Logger?.LogWarning("⏰ LoadAvailableWindowsAsyncタイムアウト: {ElapsedMs}ms", timeoutElapsed);
            Console.WriteLine($"⏰ LoadAvailableWindowsAsyncタイムアウト: {timeoutElapsed:F1}ms");

            // タイムアウト時は現在までの部分結果を保持
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                Logger?.LogDebug("⏰ タイムアウト時の部分結果保持: {Count}個", AvailableWindows.Count);
                Console.WriteLine($"⏰ タイムアウト時の部分結果保持: {AvailableWindows.Count}個");
            });
        }
        catch (Exception ex)
        {
            var errorElapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
            Logger?.LogError(ex, "❌ LoadAvailableWindowsAsyncでエラー発生 ({ElapsedMs}ms)", errorElapsed);
            Console.WriteLine($"❌ LoadAvailableWindowsAsyncでエラー発生 ({errorElapsed:F1}ms): {ex.Message}");

            // エラー時は空の一覧を表示
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                AvailableWindows.Clear();
            });
        }
        finally
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsLoading = false;
                var finalElapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                Logger?.LogDebug("🔄 IsLoading = false 設定完了 ({ElapsedMs}ms)", finalElapsed);
                Console.WriteLine($"🔄 IsLoading = false 設定完了 ({finalElapsed:F1}ms)");
            });
        }
    }

    /// <summary>
    /// ウィンドウ選択実行
    /// </summary>
    private async Task ExecuteSelectWindowAsync(WindowInfo selectedWindow)
    {
        
        try
        {
            if (selectedWindow == null)
            {
                Logger?.LogWarning("No window selected");
                return;
            }

            Logger?.LogInformation("Window selection executed: '{Title}' (Handle: {Handle})", 
                selectedWindow.Title, selectedWindow.Handle);
            
            DebugLogUtility.WriteLog($"📢 ウィンドウ選択実行: '{selectedWindow.Title}' (Handle={selectedWindow.Handle})");

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                DialogResult = selectedWindow;
                IsClosed = true;
            });

            DebugLogUtility.WriteLog($"✅ ウィンドウ選択ダイアログ完了: DialogResult設定済み");
            Logger?.LogDebug("Window selection processing completed");
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Error during window selection processing: {ErrorMessage}", ex.Message);
            DebugLogUtility.WriteLog($"❌ ウィンドウ選択処理エラー: {ex.Message}");
        }
    }

    /// <summary>
    /// キャンセル実行
    /// </summary>
    private void ExecuteCancel()
    {
        Logger?.LogDebug("Window selection cancelled");
        
        // UIスレッドで確実に実行
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            DialogResult = null;
            IsClosed = true;
        });
    }

    /// <summary>
    /// 更新実行
    /// </summary>
    internal async Task ExecuteRefreshAsync()
    {
        Console.WriteLine("🔴 [DEBUG] ExecuteRefreshAsync開始 - Logger is: " + (Logger?.ToString() ?? "NULL"));
        Logger?.LogDebug("Refreshing window list");
        Console.WriteLine("🔴 [DEBUG] ExecuteRefreshAsync - LoadAvailableWindowsAsync呼び出し前");
        await LoadAvailableWindowsAsync().ConfigureAwait(false);
        Console.WriteLine("🔴 [DEBUG] ExecuteRefreshAsync完了");
    }
}