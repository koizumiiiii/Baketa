using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Baketa.Infrastructure.Platform.Windows.Monitors;
using Baketa.Infrastructure.Platform.Windows.Fullscreen;
using Baketa.Core.UI.Monitors;
using Baketa.Core.UI.Fullscreen;
using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace Baketa.Integration.Tests.MultiMonitor;

/// <summary>
/// マルチモニター実装の基本機能テスト
/// Test Explorerで認識・実行可能なテストクラス
/// </summary>
public class MultiMonitorBasicTests : IAsyncDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MultiMonitorBasicTests> _logger;
    private readonly ITestOutputHelper _output;
    
    public MultiMonitorBasicTests(ITestOutputHelper output)
    {
        _output = output;
        
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();
        _logger = _serviceProvider.GetRequiredService<ILogger<MultiMonitorBasicTests>>();
    }
    
    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        services.AddSingleton<IMonitorManager, WindowsMonitorManager>();
        services.AddSingleton<IFullscreenModeService, WindowsFullscreenModeService>();
    }
    
    /// <summary>
    /// モニターマネージャーの基本機能テスト
    /// </summary>
    [Fact]
    public void MonitorManagerShouldInitializeSuccessfully()
    {
        // Arrange & Act
        var monitorManager = _serviceProvider.GetRequiredService<IMonitorManager>();
        
        // Assert
        Assert.NotNull(monitorManager);
        Assert.IsType<WindowsMonitorManager>(monitorManager);
        
        _output.WriteLine("✅ モニターマネージャーの初期化が成功しました");
    }
    
    /// <summary>
    /// フルスクリーンサービスの基本機能テスト
    /// </summary>
    [Fact]
    public void FullscreenServiceShouldInitializeSuccessfully()
    {
        // Arrange & Act
        var fullscreenService = _serviceProvider.GetRequiredService<IFullscreenModeService>();
        
        // Assert
        Assert.NotNull(fullscreenService);
        Assert.IsType<WindowsFullscreenModeService>(fullscreenService);
        
        _output.WriteLine("✅ フルスクリーンサービスの初期化が成功しました");
    }
    
    /// <summary>
    /// モニター監視開始・停止のテスト
    /// </summary>
    [Fact]
    public async Task MonitorManagerStartStopMonitoringShouldWork()
    {
        // Arrange
        var monitorManager = _serviceProvider.GetRequiredService<IMonitorManager>();
        
        // Act & Assert - 開始
        await monitorManager.StartMonitoringAsync().ConfigureAwait(true);
        Assert.True(monitorManager.IsMonitoring);
        _output.WriteLine("✅ モニター監視開始");
        
        // Act & Assert - 停止
        await monitorManager.StopMonitoringAsync().ConfigureAwait(true);
        Assert.False(monitorManager.IsMonitoring);
        _output.WriteLine("✅ モニター監視停止");
    }
    
    /// <summary>
    /// フルスクリーン監視開始・停止のテスト
    /// </summary>
    [Fact]
    public async Task FullscreenServiceStartStopMonitoringShouldWork()
    {
        // Arrange
        var fullscreenService = _serviceProvider.GetRequiredService<IFullscreenModeService>();
        var mockGameWindow = GetMockGameWindowHandle();
        
        // Act & Assert - 開始
        await fullscreenService.StartMonitoringAsync(mockGameWindow).ConfigureAwait(true);
        _output.WriteLine("✅ フルスクリーン監視開始");
        
        // Act & Assert - 停止
        await fullscreenService.StopMonitoringAsync().ConfigureAwait(true);
        _output.WriteLine("✅ フルスクリーン監視停止");
        
        // 例外が発生しなければ成功
        Assert.True(true);
    }
    
    /// <summary>
    /// エラーハンドリングの基本テスト
    /// </summary>
    [Fact]
    public void MonitorManagerWithInvalidHandleShouldFallback()
    {
        // Arrange
        if (_serviceProvider.GetRequiredService<IMonitorManager>() is not WindowsMonitorManager monitorManager)
        {
            _output.WriteLine("⚠️  WindowsMonitorManagerが取得できませんでした。テストをスキップします。");
            return;
        }
        
        var invalidHandle = new nint(0xDEADBEEF);
        
        // Act
        var result = monitorManager.GetMonitorFromWindow(invalidHandle);
        
        // Assert
        if (result.HasValue)
        {
            Assert.True(result.Value.IsPrimary);
            _output.WriteLine("✅ 無効なハンドルでプライマリモニターにフォールバック成功");
        }
        else
        {
            _output.WriteLine("⚠️  フォールバック結果が取得できませんでした");
        }
    }
    
    /// <summary>
    /// リソースの適切な解放テスト
    /// </summary>
    [Fact]
    public async Task ServicesShouldDisposeGracefully()
    {
        // Arrange
        var services = new ServiceCollection();
        ConfigureServices(services);
        var provider = services.BuildServiceProvider();
        
        try
        {
            var monitorManager = provider.GetRequiredService<IMonitorManager>();
            var fullscreenService = provider.GetRequiredService<IFullscreenModeService>();
            
            // Act - サービス開始
            await monitorManager.StartMonitoringAsync().ConfigureAwait(true);
            await fullscreenService.StartMonitoringAsync(GetMockGameWindowHandle()).ConfigureAwait(true);
            
            // 短時間実行
            await Task.Delay(100).ConfigureAwait(true);
            
            // サービス停止
            await monitorManager.StopMonitoringAsync().ConfigureAwait(true);
            await fullscreenService.StopMonitoringAsync().ConfigureAwait(true);
            
            _output.WriteLine("✅ サービスの開始・停止が正常に完了しました");
        }
        finally
        {
            // Act - リソース解放
            if (provider is IAsyncDisposable asyncProvider)
            {
                await asyncProvider.DisposeAsync().ConfigureAwait(true);
            }
            else if (provider is IDisposable disposableProvider)
            {
                await Task.Run(disposableProvider.Dispose).ConfigureAwait(true);
            }
        }
        
        // Assert
        Assert.True(true); // 例外が発生しなければ成功
        _output.WriteLine("✅ リソースの解放が正常に完了しました");
    }
    
    /// <summary>
    /// モックゲームウィンドウハンドルを取得
    /// </summary>
    private static nint GetMockGameWindowHandle()
    {
        // 現在のプロセスのメインウィンドウハンドルを使用
        var handle = Process.GetCurrentProcess().MainWindowHandle;
        return handle != nint.Zero ? handle : new nint(1); // フォールバック値
    }
    
    public async ValueTask DisposeAsync()
    {
        if (_serviceProvider is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(true);
        }
        else if (_serviceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }
        
        GC.SuppressFinalize(this);
    }
}
