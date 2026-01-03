using System.Reactive;
using Baketa.Core.Abstractions.CrashReporting;
using Baketa.Core.Logging;
using Microsoft.Extensions.Logging;
using ReactiveUI;

namespace Baketa.UI.Services;

/// <summary>
/// [Issue #252] アプリケーション全体の例外ハンドラ
/// 3種類の未処理例外を捕捉してCrashReportServiceに統合
/// </summary>
public sealed class GlobalExceptionHandler : IDisposable
{
    private readonly ICrashReportService? _crashReportService;
    private readonly ILogger<GlobalExceptionHandler>? _logger;
    private bool _isInitialized;
    private bool _disposed;

    /// <summary>
    /// シングルトンインスタンス
    /// </summary>
    public static GlobalExceptionHandler? Instance { get; private set; }

    /// <summary>
    /// GlobalExceptionHandlerを初期化
    /// </summary>
    public GlobalExceptionHandler(
        ICrashReportService? crashReportService = null,
        ILogger<GlobalExceptionHandler>? logger = null)
    {
        _crashReportService = crashReportService;
        _logger = logger;
    }

    /// <summary>
    /// シングルトンを初期化
    /// </summary>
    public static void Initialize(ICrashReportService? crashReportService = null, ILogger<GlobalExceptionHandler>? logger = null)
    {
        Instance?.Dispose();
        Instance = new GlobalExceptionHandler(crashReportService, logger);
        Instance.RegisterHandlers();
    }

    /// <summary>
    /// 3種類の例外ハンドラを登録
    /// </summary>
    public void RegisterHandlers()
    {
        if (_isInitialized)
        {
            _logger?.LogWarning("[Issue #252] GlobalExceptionHandler既に初期化済み");
            return;
        }

        // 1. AppDomain未処理例外ハンドラ
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;

        // 2. TaskScheduler未監視タスク例外ハンドラ
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // 3. ReactiveUI例外ハンドラ
        RxApp.DefaultExceptionHandler = Observer.Create<Exception>(OnReactiveUIException);

        _isInitialized = true;
        _logger?.LogInformation("[Issue #252] GlobalExceptionHandler初期化完了 - 3種類の例外ハンドラ登録済み");
        RingBufferLogger.Instance.LogInfo("[Issue #252] GlobalExceptionHandler初期化完了", nameof(GlobalExceptionHandler));
    }

    /// <summary>
    /// AppDomain未処理例外ハンドラ
    /// </summary>
    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is not Exception exception)
        {
            Console.WriteLine($"💥 [AppDomain] 非例外オブジェクト: {e.ExceptionObject}");
            return;
        }

        Console.WriteLine($"💥 [AppDomain] 未処理例外: {exception.GetType().Name}: {exception.Message}");
        Console.WriteLine($"💥 [AppDomain] IsTerminating: {e.IsTerminating}");

        // リングバッファに記録
        RingBufferLogger.Instance.LogError(
            $"[AppDomain] {exception.Message}",
            nameof(GlobalExceptionHandler),
            exception);

        // クラッシュレポート生成（プロセス終了時は同期実行）
        if (e.IsTerminating)
        {
            try
            {
                GenerateCrashReportSync(exception, "AppDomain.UnhandledException (Terminating)");
            }
            catch (Exception reportEx)
            {
                Console.WriteLine($"💥 クラッシュレポート生成失敗: {reportEx.Message}");
            }
        }
        else
        {
            // 非終了時は非同期で処理
            _ = GenerateCrashReportAsync(exception, "AppDomain.UnhandledException");
        }
    }

    /// <summary>
    /// TaskScheduler未監視タスク例外ハンドラ
    /// </summary>
    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        var exception = e.Exception;
        Console.WriteLine($"💥 [TaskScheduler] 未監視タスク例外: {exception.GetType().Name}: {exception.Message}");

        // リングバッファに記録
        RingBufferLogger.Instance.LogError(
            $"[TaskScheduler] {exception.Message}",
            nameof(GlobalExceptionHandler),
            exception);

        // 例外を観察済みとしてマーク（プロセス終了を防止）
        e.SetObserved();

        // クラッシュレポート生成
        _ = GenerateCrashReportAsync(exception, "TaskScheduler.UnobservedTaskException");
    }

    /// <summary>
    /// ReactiveUI例外ハンドラ
    /// </summary>
    private void OnReactiveUIException(Exception exception)
    {
        Console.WriteLine($"🚨 [ReactiveUI] 例外: {exception.GetType().Name}: {exception.Message}");

        // リングバッファに記録
        RingBufferLogger.Instance.LogError(
            $"[ReactiveUI] {exception.Message}",
            nameof(GlobalExceptionHandler),
            exception);

        // UIスレッド違反例外は詳細ログを出力
        if (exception is InvalidOperationException && exception.Message.Contains("thread"))
        {
            Console.WriteLine($"🧵 UIスレッド違反詳細: {exception.StackTrace}");
        }

        // ReactiveUI例外は通常致命的ではないため、レポートは生成するが終了しない
        _ = GenerateCrashReportAsync(exception, "RxApp.DefaultExceptionHandler");
    }

    /// <summary>
    /// クラッシュレポートを非同期で生成
    /// </summary>
    private async Task GenerateCrashReportAsync(Exception exception, string source)
    {
        if (_crashReportService == null)
        {
            _logger?.LogWarning("[Issue #252] CrashReportService未設定 - レポート生成をスキップ");
            return;
        }

        try
        {
            var report = await _crashReportService.GenerateCrashReportAsync(exception, source)
                .ConfigureAwait(false);

            var filePath = await _crashReportService.SaveCrashReportAsync(report)
                .ConfigureAwait(false);

            await _crashReportService.CreateCrashPendingFlagAsync()
                .ConfigureAwait(false);

            _logger?.LogInformation("[Issue #252] クラッシュレポート生成完了: {FilePath}", filePath);
            Console.WriteLine($"📝 クラッシュレポート生成完了: {filePath}");
        }
        catch (Exception reportEx)
        {
            _logger?.LogError(reportEx, "[Issue #252] クラッシュレポート生成失敗");
            Console.WriteLine($"💥 クラッシュレポート生成失敗: {reportEx.Message}");
        }
    }

    /// <summary>
    /// クラッシュレポートを同期で生成（プロセス終了時用）
    /// </summary>
    private void GenerateCrashReportSync(Exception exception, string source)
    {
        if (_crashReportService == null)
        {
            _logger?.LogWarning("[Issue #252] CrashReportService未設定 - レポート生成をスキップ");
            return;
        }

        try
        {
            // プロセス終了時は同期的に実行（タイムアウト5秒）
            var task = Task.Run(async () =>
            {
                var report = await _crashReportService.GenerateCrashReportAsync(exception, source)
                    .ConfigureAwait(false);

                var filePath = await _crashReportService.SaveCrashReportAsync(report)
                    .ConfigureAwait(false);

                await _crashReportService.CreateCrashPendingFlagAsync()
                    .ConfigureAwait(false);

                return filePath;
            });

            if (task.Wait(TimeSpan.FromSeconds(5)))
            {
                Console.WriteLine($"📝 クラッシュレポート生成完了: {task.Result}");
            }
            else
            {
                Console.WriteLine("⚠️ クラッシュレポート生成タイムアウト（5秒）");
            }
        }
        catch (Exception reportEx)
        {
            Console.WriteLine($"💥 クラッシュレポート生成失敗: {reportEx.Message}");
        }
    }

    /// <summary>
    /// リソースを解放
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_isInitialized)
        {
            AppDomain.CurrentDomain.UnhandledException -= OnAppDomainUnhandledException;
            TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        }

        if (Instance == this)
        {
            Instance = null;
        }
    }
}
