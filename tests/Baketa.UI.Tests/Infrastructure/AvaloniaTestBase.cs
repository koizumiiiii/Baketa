using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using System;
using System.Threading;
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

    protected AvaloniaTestBase()
    {
        InitializeAvalonia();
    }

    /// <summary>
    /// Avalonia UIフレームワークを初期化
    /// </summary>
    private static void InitializeAvalonia()
    {
        lock (_initLock)
        {
            if (_initialized)
                return;

            try
            {
                // Headlessモードでアプリケーションを初期化
                AppBuilder.Configure<TestApplication>()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions())
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
        // 基底クラスでは何もしない
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
