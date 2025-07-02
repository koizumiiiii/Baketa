using Baketa.Core.UI.Geometry;
using Baketa.Core.UI.Monitors;
using Baketa.Core.UI.Overlay.Positioning;
using Baketa.UI.DI.Modules;
using Baketa.UI.Overlay.Positioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;
using Baketa.Core.UI.Fullscreen;
using Baketa.UI.Overlay;
using Baketa.Core.UI.Overlay;
using Baketa.UI.Monitors;
using Baketa.UI.Overlay.MultiMonitor;

// 型エイリアス定義
using CorePoint = Baketa.Core.UI.Geometry.Point;
using CoreRect = Baketa.Core.UI.Geometry.Rect;
using CoreSize = Baketa.Core.UI.Geometry.Size;
using Geometry = Baketa.Core.UI.Geometry;

namespace Baketa.Core.Tests.OverlayPositioning;

/// <summary>
/// オーバーレイ位置管理システムの統合テスト
/// Issue #69 の完全なワークフローをテスト
/// </summary>
public sealed class OverlayPositioningIntegrationTests : IAsyncDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IServiceCollection _services;
    
    public OverlayPositioningIntegrationTests()
    {
        _services = new ServiceCollection();
        
        // 必要な基本サービスを登録
        _services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        
        // モックサービスの登録（オーバーレイモジュールより先に登録）
        _services.AddSingleton<IFullscreenModeService, MockFullscreenModeService>();
        _services.AddSingleton<IOverlayWindowManager, MockOverlayWindowManager>();
        _services.AddSingleton<IMonitorManager, MockMonitorManager>();
        
        // オーバーレイ位置管理システムを登録
        OverlayPositioningModule.RegisterServices(_services);
        
        // テスト用のモックアダプターサービスでオーバーライド（最後に登録）
        // .NET DIでは最後に登録されたサービスが優先される
        _services.RemoveAll<ITextMeasurementService>();
        _services.AddSingleton<ITextMeasurementService, MockTextMeasurementService>();
        
        _serviceProvider = _services.BuildServiceProvider();
    }
    
    [Fact]
    public async Task ServiceRegistration_ShouldRegisterAllRequiredServices()
    {
        // Act & Assert
        // ファクトリーパターンで作成されるため、直接はIOverlayPositionManagerは取得できない
        Assert.NotNull(_serviceProvider.GetService<ITextMeasurementService>());
        Assert.NotNull(_serviceProvider.GetService<IOverlayPositionManagerFactory>());
        Assert.NotNull(_serviceProvider.GetService<MultiMonitorOverlayManager>());
        
        // ファクトリーからの作成もテスト
        var factory = _serviceProvider.GetRequiredService<IOverlayPositionManagerFactory>();
        await using var positionManager = await factory.CreateAsync();
        Assert.NotNull(positionManager);
        
        // モックサービスの確認
        Assert.IsType<MockTextMeasurementService>(_serviceProvider.GetService<ITextMeasurementService>());
        Assert.IsType<MockMonitorManager>(_serviceProvider.GetService<IMonitorManager>());
        Assert.IsType<MockOverlayWindowManager>(_serviceProvider.GetService<IOverlayWindowManager>());
        Assert.IsType<MockFullscreenModeService>(_serviceProvider.GetService<IFullscreenModeService>());
    }
    
    [Fact]
    public async Task FullWorkflow_OcrToTranslationToPositioning_ShouldWorkEndToEnd()
    {
        // Arrange
        var factory = _serviceProvider.GetRequiredService<IOverlayPositionManagerFactory>();
        var settings = OverlayPositionSettings.ForTranslation;
        
        await using var positionManager = await factory.CreateWithSettingsAsync(settings);
        
        var positionUpdateReceived = false;
        OverlayPositionUpdatedEventArgs? lastUpdate = null;
        
        positionManager.PositionUpdated += (_, args) =>
        {
            positionUpdateReceived = true;
            lastUpdate = args;
        };
        
        // Step 1: OCR検出をシミュレート
        var textRegions = new List<TextRegion>
        {
            new(
                Bounds: new CoreRect(100, 200, 300, 50),
                Text: "Hello, world!",
                Confidence: 0.95,
                DetectedAt: DateTimeOffset.Now
            )
        };
        
        await positionManager.UpdateTextRegionsAsync(textRegions);
        
        // Step 2: 翻訳完了をシミュレート
        var translationInfo = new TranslationInfo
        {
            SourceText = "Hello, world!",
            TranslatedText = "こんにちは、世界！",
            SourceRegion = textRegions[0],
            TranslationId = Guid.NewGuid(),
            MeasurementInfo = new TextMeasurementInfo(
                TextSize: new CoreSize(180, 40),
                LineCount: 1,
                CharacterCount: 9,
                UsedFontSize: 16,
                FontFamily: "Yu Gothic UI",
                MeasuredAt: DateTimeOffset.Now
            )
        };
        
        await positionManager.UpdateTranslationInfoAsync(translationInfo);
        
        // Step 3: 位置・サイズ計算
        var positionInfo = await positionManager.CalculatePositionAndSizeAsync();
        
        // 非同期イベントの完了を待機
        await Task.Delay(200);
        
        // Assert
        Assert.True(positionUpdateReceived, "位置更新イベントが発火されませんでした");
        Assert.NotNull(lastUpdate);
        Assert.Equal(PositionUpdateReason.TranslationCompleted, lastUpdate.Reason);
        
        Assert.True(positionInfo.IsValid, "位置情報が無効です");
        Assert.Equal(textRegions[0], positionInfo.SourceTextRegion);
        Assert.True(positionInfo.Position.Y > textRegions[0].Bounds.Bottom, "翻訳テキストが原文の下に配置されていません");
        
        // サイズが測定情報に基づいて計算されていることを確認
        Assert.True(positionInfo.Size.Width >= translationInfo.MeasurementInfo.Value.TextSize.Width);
        Assert.True(positionInfo.Size.Height >= translationInfo.MeasurementInfo.Value.TextSize.Height);
    }
    
    [Fact]
    public async Task GameWindowUpdate_ShouldTriggerPositionRecalculation()
    {
        // Arrange
        var factory = _serviceProvider.GetRequiredService<IOverlayPositionManagerFactory>();
        await using var positionManager = await factory.CreateAsync();
        
        var updateCount = 0;
        positionManager.PositionUpdated += (_, _) => updateCount++;
        
        // Act
        var gameWindowInfo = new GameWindowInfo
        {
            WindowHandle = new nint(12345),
            WindowTitle = "Test Game",
            Position = new CorePoint(0, 0),
            Size = new CoreSize(1920, 1080),
            ClientPosition = new CorePoint(0, 0),
            ClientSize = new CoreSize(1920, 1080),
            IsFullScreen = false,
            IsMaximized = true,
            IsMinimized = false,
            IsActive = true,
            Monitor = CreateTestMonitor()
        };
        
        await positionManager.NotifyGameWindowUpdateAsync(gameWindowInfo);
        
        // 非同期処理の完了を待機
        await Task.Delay(100);
        
        // Assert
        Assert.True(updateCount > 0, "ゲームウィンドウ更新時に位置更新イベントが発火されませんでした");
    }
    
    [Fact]
    public async Task PositionManagerFactory_WithDifferentSettings_ShouldCreateConfiguredManagers()
    {
        // Arrange
        var factory = _serviceProvider.GetRequiredService<IOverlayPositionManagerFactory>();
        
        // Act
        await using var defaultManager = await factory.CreateAsync();
        await using var translationManager = await factory.CreateWithSettingsAsync(OverlayPositionSettings.ForTranslation);
        await using var debugManager = await factory.CreateWithSettingsAsync(OverlayPositionSettings.ForDebug);
        
        // Assert
        Assert.Equal(OverlayPositionSettings.ForTranslation.PositionMode, defaultManager.PositionMode);
        Assert.Equal(OverlayPositionSettings.ForTranslation.PositionMode, translationManager.PositionMode);
        Assert.Equal(OverlayPositionSettings.ForDebug.PositionMode, debugManager.PositionMode);
        
        Assert.Equal(OverlayPositionSettings.ForTranslation.SizeMode, defaultManager.SizeMode);
        Assert.Equal(OverlayPositionSettings.ForTranslation.SizeMode, translationManager.SizeMode);
        Assert.Equal(OverlayPositionSettings.ForDebug.SizeMode, debugManager.SizeMode);
    }
    
    [Fact]
    public async Task TextMeasurementService_Integration_ShouldWorkWithPositionManager()
    {
        // Arrange
        var textMeasurementService = _serviceProvider.GetRequiredService<ITextMeasurementService>();
        var factory = _serviceProvider.GetRequiredService<IOverlayPositionManagerFactory>();
        await using var positionManager = await factory.CreateAsync();
        
        positionManager.SizeMode = OverlaySizeMode.ContentBased;
        
        // Act
        var measurementResult = await textMeasurementService.MeasureTextAsync(
            "テスト用の日本語テキスト",
            TextMeasurementOptions.Default with
            {
                FontFamily = "Yu Gothic UI",
                FontSize = 16,
                FontWeight = "Normal"
            }
        );
        
        var translationInfo = new TranslationInfo
        {
            SourceText = "Test text",
            TranslatedText = "テスト用の日本語テキスト",
            MeasurementInfo = new TextMeasurementInfo(
                measurementResult.Size,
                measurementResult.LineCount,
                "テスト用の日本語テキスト".Length,
                measurementResult.ActualFontSize,
                measurementResult.MeasuredWith.FontFamily,
                DateTimeOffset.Now
            )
        };
        
        await positionManager.UpdateTranslationInfoAsync(translationInfo);
        var positionInfo = await positionManager.CalculatePositionAndSizeAsync();
        
        // Assert
        Assert.True(measurementResult.IsValid);
        Assert.True(positionInfo.IsValid);
        
        // 測定結果が位置計算に反映されていることを確認
        Assert.True(positionInfo.Size.Width >= measurementResult.Size.Width);
        Assert.True(positionInfo.Size.Height >= measurementResult.Size.Height);
    }
    
    [Fact]
    public void OverlayPositionSettings_Variations_ShouldHaveCorrectDefaults()
    {
        // Act & Assert
        var translationSettings = OverlayPositionSettings.ForTranslation;
        Assert.Equal(OverlayPositionMode.OcrRegionBased, translationSettings.PositionMode);
        Assert.Equal(OverlaySizeMode.ContentBased, translationSettings.SizeMode);
        Assert.Equal(new Baketa.Core.UI.Geometry.CoreVector(0, 5), translationSettings.PositionOffset);
        
        var debugSettings = OverlayPositionSettings.ForDebug;
        Assert.Equal(OverlayPositionMode.Fixed, debugSettings.PositionMode);
        Assert.Equal(OverlaySizeMode.Fixed, debugSettings.SizeMode);
        
        // 基本的な設定値の確認
        Assert.True(translationSettings.MaxSize.Width > 0);
        Assert.True(translationSettings.MaxSize.Height > 0);
        Assert.True(translationSettings.MinSize.Width > 0);
        Assert.True(translationSettings.MinSize.Height > 0);
    }
    
    [Fact]
    public async Task ErrorHandling_WithInvalidInput_ShouldNotCrash()
    {
        // Arrange
        var factory = _serviceProvider.GetRequiredService<IOverlayPositionManagerFactory>();
        await using var positionManager = await factory.CreateAsync();
        
        // Act & Assert - null引数
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => positionManager.UpdateTextRegionsAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => positionManager.UpdateTranslationInfoAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => positionManager.NotifyGameWindowUpdateAsync(null!));
        
        // Act & Assert - 空のリスト（正常ケース）
        await positionManager.UpdateTextRegionsAsync([]);
        var result = await positionManager.CalculatePositionAndSizeAsync();
        Assert.True(result.IsValid); // 空でも有効な結果を返すべき
    }
    
    private static MonitorInfo CreateTestMonitor()
    {
        return new MonitorInfo(
            Handle: nint.Zero,
            Name: "Test Monitor",
            DeviceId: "TEST_MONITOR",
            Bounds: new CoreRect(0, 0, 1920, 1080),
            WorkArea: new CoreRect(0, 0, 1920, 1040),
            IsPrimary: true,
            DpiX: 96,
            DpiY: 96
        );
    }
    
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_serviceProvider is not null)
            {
                await _serviceProvider.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Use DisposeAsync"))
        {
            // IAsyncDisposableのみを実装しているサービスがある場合のエラーをキャッチ
            // このエラーは、非同期でDisposeしているので無視できる
        }
        finally
        {
            GC.SuppressFinalize(this);
        }
    }
}

/// <summary>
/// テスト用のモックモニターマネージャー
/// </summary>
internal sealed class MockMonitorManager : IMonitorManager
{
    private readonly MonitorInfo _mockMonitor = new(
        Handle: new nint(1),
        Name: "Mock Monitor",
        DeviceId: "MOCK_MONITOR",
        Bounds: new CoreRect(0, 0, 1920, 1080),
        WorkArea: new CoreRect(0, 0, 1920, 1040),
        IsPrimary: true,
        DpiX: 96,
        DpiY: 96
    );
    
    public IReadOnlyList<MonitorInfo> Monitors => [_mockMonitor];
    public MonitorInfo? PrimaryMonitor => _mockMonitor;
    public int MonitorCount => 1;
    public bool IsMonitoring { get; private set; }
    
    /// <summary>
    /// モニター設定変更イベント
    /// </summary>
#pragma warning disable CS0067 // イベントは使用されていません - モック実装のため
    public event EventHandler<MonitorChangedEventArgs>? MonitorChanged;
#pragma warning restore CS0067
    
    public MonitorInfo? GetMonitorFromWindow(nint windowHandle) => _mockMonitor;
    public MonitorInfo? GetMonitorFromPoint(CorePoint point) => _mockMonitor;
    public MonitorInfo DetermineOptimalMonitor(nint windowHandle) => _mockMonitor;
    
    public IReadOnlyList<MonitorInfo> GetMonitorsFromRect(CoreRect rect)
    {
        // モックでは常に単一のモニターを返す
        return [_mockMonitor];
    }
    
    public MonitorInfo? GetMonitorByHandle(nint handle)
    {
        // モックハンドルと一致する場合のみモニターを返す
        return handle == _mockMonitor.Handle ? _mockMonitor : null;
    }
    
    public CorePoint TransformPointBetweenMonitors(
        CorePoint point, 
        MonitorInfo sourceMonitor, 
        MonitorInfo targetMonitor)
    {
        // モックでは座標変換なし（同一座標系と仮定）
        return point;
    }
    
    public CoreRect TransformRectBetweenMonitors(CoreRect rect, MonitorInfo sourceMonitor, MonitorInfo targetMonitor)
    {
        return rect; // モックでは変換なし
    }
    
    public Task RefreshMonitorsAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
    
    public Task StartMonitoringAsync(CancellationToken cancellationToken = default)
    {
        IsMonitoring = true;
        return Task.CompletedTask;
    }
    
    public Task StopMonitoringAsync(CancellationToken cancellationToken = default)
    {
        IsMonitoring = false;
        return Task.CompletedTask;
    }
    
    public void Dispose()
    {
        IsMonitoring = false;
    }
}

/// <summary>
/// テスト用のモックフルスクリーンモードサービス
/// </summary>
internal sealed class MockFullscreenModeService : IFullscreenModeService
{
    public bool IsExclusiveFullscreen { get; private set; }
    public bool IsBorderlessFullscreen { get; private set; }
    public bool CanShowOverlay { get; private set; } = true;
    public FullscreenModeType CurrentModeType { get; private set; } = FullscreenModeType.Windowed;
    public nint TargetWindowHandle { get; private set; }
    
    /// <summary>
    /// フルスクリーンモード変更イベント
    /// </summary>
#pragma warning disable CS0067 // イベントは使用されていません - モック実装のため
    public event EventHandler<FullscreenModeChangedEventArgs>? FullscreenModeChanged;
#pragma warning restore CS0067
    
    public FullscreenModeChangedEventArgs DetectFullscreenMode(nint windowHandle, MonitorInfo? targetMonitor = null)
    {
        var args = new FullscreenModeChangedEventArgs(
            IsExclusiveFullscreen: false,
            IsBorderlessFullscreen: false,
            CanShowOverlay: true,
            RecommendationMessage: "モックモード",
            AffectedMonitor: targetMonitor
        );
        return args;
    }
    
    public Task StartMonitoringAsync(nint windowHandle, CancellationToken cancellationToken = default)
    {
        TargetWindowHandle = windowHandle;
        return Task.CompletedTask;
    }
    
    public Task StopMonitoringAsync(CancellationToken cancellationToken = default)
    {
        TargetWindowHandle = nint.Zero;
        return Task.CompletedTask;
    }
    
    public Task ShowRecommendationAsync(FullscreenModeChangedEventArgs currentMode)
    {
        return Task.CompletedTask;
    }
    
    public Task RefreshModeAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
    
    public void Dispose()
    {
        // Mock implementation
    }
}

/// <summary>
/// テスト用のモックオーバーレイウィンドウマネージャー
/// </summary>
internal sealed class MockOverlayWindowManager : IOverlayWindowManager
{
    private readonly Dictionary<nint, IOverlayWindow> _overlays = [];
    private int _nextHandle = 1000;
    
    public int ActiveOverlayCount => _overlays.Count;
    
    public async Task<IOverlayWindow> CreateOverlayWindowAsync(
        nint targetWindowHandle, 
        Geometry.Size initialSize, 
        Geometry.Point initialPosition)
    {
        await Task.Yield();
        
        var handle = new nint(_nextHandle++);
        var overlay = new MockOverlayWindow(handle, 
            new CoreSize(initialSize.Width, initialSize.Height), 
            new CorePoint(initialPosition.X, initialPosition.Y));
        _overlays[handle] = overlay;
        
        return overlay;
    }
    
    public IOverlayWindow? GetOverlayWindow(nint handle)
    {
        return _overlays.TryGetValue(handle, out var overlay) ? overlay : null;
    }
    
    public async Task CloseAllOverlaysAsync()
    {
        await Task.Yield();
        
        foreach (var overlay in _overlays.Values)
        {
            overlay.Close();
        }
        _overlays.Clear();
    }
    
    public void Dispose()
    {
        foreach (var overlay in _overlays.Values)
        {
            overlay.Close();
        }
        _overlays.Clear();
    }
}

/// <summary>
/// テスト用のモックオーバーレイウィンドウ
/// </summary>
internal sealed class MockOverlayWindow : IOverlayWindow
{
    private readonly List<Geometry.Rect> _hitTestAreas = [];
    
    public nint Handle { get; }
    public CoreSize Size { get; set; }
    public CorePoint Position { get; set; }
    public bool IsVisible { get; private set; } = true;
    public double Opacity { get; } = 0.9;
    public bool IsClickThrough { get; set; } = true;
    public IReadOnlyList<Geometry.Rect> HitTestAreas => _hitTestAreas.AsReadOnly();
    public nint TargetWindowHandle { get; set; }
    
    // IOverlayWindowインターフェースとの型変換
    Geometry.Point IOverlayWindow.Position 
    { 
        get => new(Position.X, Position.Y); 
        set => Position = new CorePoint(value.X, value.Y); 
    }
    
    Geometry.Size IOverlayWindow.Size 
    { 
        get => new(Size.Width, Size.Height); 
        set => Size = new CoreSize(value.Width, value.Height); 
    }
    
    public MockOverlayWindow(nint handle, CoreSize size, CorePoint position)
    {
        Handle = handle;
        Size = size;
        Position = position;
    }
    
    public void Show()
    {
        IsVisible = true;
    }
    
    public void Hide()
    {
        IsVisible = false;
    }
    
    public void Close()
    {
        IsVisible = false;
    }
    
    public void AddHitTestArea(Geometry.Rect area)
    {
        _hitTestAreas.Add(area);
    }
    
    public void RemoveHitTestArea(Geometry.Rect area)
    {
        _hitTestAreas.Remove(area);
    }
    
    public void ClearHitTestAreas()
    {
        _hitTestAreas.Clear();
    }
    
    public void UpdateContent(object? content = null)
    {
        // Mock implementation
    }
    
    public void AdjustToTargetWindow()
    {
        // Mock implementation
    }
    
    public void Dispose()
    {
        Close();
    }
}

/// <summary>
/// テスト用のモックテキスト測定サービス
/// </summary>
internal sealed class MockTextMeasurementService : ITextMeasurementService
{
    public Task<TextMeasurementResult> MeasureTextAsync(string text, TextMeasurementOptions options, CancellationToken cancellationToken = default)
    {
        // モックではテキストの文字数をベースにサイズを計算
        var charactersPerLine = Math.Max(1, (int)(options.MaxWidth / (options.FontSize * 0.6))); // おおよその文字幅
        var lineCount = Math.Max(1, (int)Math.Ceiling((double)text.Length / charactersPerLine));
        
        var width = Math.Min(text.Length * options.FontSize * 0.6, options.MaxWidth);
        var height = lineCount * options.FontSize * 1.2; // 行間考慮
        
        var result = new TextMeasurementResult(
            Size: new CoreSize(width, height),
            LineCount: lineCount,
            ActualFontSize: options.FontSize,
            MeasuredWith: options
        );
        
        return Task.FromResult(result);
    }
}
