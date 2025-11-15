using System;
using System.Reactive.Concurrency;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using ReactiveUI;
using Xunit;

namespace Baketa.UI.Tests.Infrastructure;

/// <summary>
/// Avalonia UIテスト用の基底クラス
/// UIコンポーネントのテストに必要な初期化処理を提供
/// </summary>
public abstract class AvaloniaTestBase : IDisposable
{
    private static readonly object _initLock = new();
    private static bool _initialized;
    private readonly object _instanceLock = new();
    private bool _instanceInitialized;

    protected AvaloniaTestBase()
    {
        InitializeAvalonia();
        InitializeReactiveUI();
    }

    /// <summary>
    /// Avalonia UIフレームワークを初期化
    /// </summary>
    private void InitializeAvalonia()
    {
        lock (_instanceLock)
        {
            if (_instanceInitialized)
                return;

            lock (_initLock)
            {
                if (!_initialized)
                {
                    try
                    {
                        // Headlessモードでアプリケーションを初期化
                        var headlessOptions = new AvaloniaHeadlessPlatformOptions
                        {
                            UseHeadlessDrawing = false, // 描画処理を無効化してパフォーマンス向上
                        };

                        AppBuilder.Configure<TestApplication>()
                            .UseHeadless(headlessOptions)
                            .SetupWithoutStarting();

                        _initialized = true;
                    }
                    catch (InvalidOperationException)
                    {
                        // 既に初期化済みの場合は無視
                        _initialized = true;
                    }
                }
            }

            _instanceInitialized = true;
        }
    }

    /// <summary>
    /// ReactiveUIのスケジューラーを初期化
    /// </summary>
    private static void InitializeReactiveUI()
    {
        try
        {
            // テスト環境でのReactiveUIスケジューラー設定
            // CurrentThreadSchedulerを使用することで同期的にテストを実行
            RxApp.MainThreadScheduler = CurrentThreadScheduler.Instance;
            RxApp.TaskpoolScheduler = ImmediateScheduler.Instance;
        }
        catch (Exception ex)
        {
            // ReactiveUI初期化エラーをデバッグ出力（テスト実行には影響しない）
            System.Diagnostics.Debug.WriteLine($"ReactiveUI initialization warning: {ex.Message}");
        }
    }

    /// <summary>
    /// UIスレッドでアクションを実行
    /// </summary>
    /// <param name="action">実行するアクション</param>
    protected static void RunOnUIThread(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Invoke(action);
        }
    }

    /// <summary>
    /// UIスレッドでファンクションを実行
    /// </summary>
    /// <typeparam name="T">戻り値の型</typeparam>
    /// <param name="func">実行するファンクション</param>
    /// <returns>実行結果</returns>
    protected static T RunOnUIThread<T>(Func<T> func)
    {
        ArgumentNullException.ThrowIfNull(func);

        if (Dispatcher.UIThread.CheckAccess())
        {
            return func();
        }
        else
        {
            return Dispatcher.UIThread.Invoke(func);
        }
    }

    public virtual void Dispose()
    {
        // インスタンス固有のクリーンアップ
        lock (_instanceLock)
        {
            _instanceInitialized = false;
        }

        // ガベージコレクションの実行を促す
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// テスト用アプリケーション
/// </summary>
public class TestApplication : Avalonia.Application
{
    public override void Initialize()
    {
        // テスト用の最小限の初期化
    }
}
