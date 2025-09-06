using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Baketa.Core.Abstractions.UI;
using Baketa.Core.Abstractions.UI.Overlay;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Events.EventTypes;
using Baketa.UI.DI.Modules;
using Baketa.UI.Services.Overlay;
using Baketa.UI.Services;
using Baketa.Application.DI.Modules;

namespace Baketa.UI.Tests.Services.Overlay;

/// <summary>
/// 統一システム動作検証テスト
/// Interface Implementation Replacement戦略の実装確認
/// </summary>
public class UnifiedSystemIntegrationTest
{
    [Fact]
    public void Phase16UIOverlayModule_Should_RegisterAvaloniaOverlayRenderer_WithAllInterfaces()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        
        // 依存するサービスをモックで追加
        services.AddSingleton<InPlaceTranslationOverlayManager>();
        
        var phase16Module = new Phase16UIOverlayModule();
        
        // Act
        phase16Module.RegisterServices(services);
        var serviceProvider = services.BuildServiceProvider();
        
        // Assert - DI登録確認
        var overlayRenderer = serviceProvider.GetService<IOverlayRenderer>();
        var overlayManager = serviceProvider.GetService<IInPlaceTranslationOverlayManager>();
        var eventProcessor = serviceProvider.GetService<IEventProcessor<OverlayUpdateEvent>>();
        
        Assert.NotNull(overlayRenderer);
        Assert.NotNull(overlayManager);
        Assert.NotNull(eventProcessor);
        
        // 同一インスタンス確認（重要：Interface Implementation Replacement戦略）
        Assert.Same(overlayRenderer, overlayManager);
        Assert.Same(overlayManager, eventProcessor);
        
        // 型確認
        Assert.IsType<AvaloniaOverlayRenderer>(overlayRenderer);
        Assert.IsType<AvaloniaOverlayRenderer>(overlayManager);
        Assert.IsType<AvaloniaOverlayRenderer>(eventProcessor);
    }
    
    [Fact]
    public void AvaloniaOverlayRenderer_Should_ImplementAllRequiredInterfaces()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<InPlaceTranslationOverlayManager>();
        
        var phase16Module = new Phase16UIOverlayModule();
        phase16Module.RegisterServices(services);
        var serviceProvider = services.BuildServiceProvider();
        
        // Act
        var renderer = serviceProvider.GetRequiredService<IOverlayRenderer>() as AvaloniaOverlayRenderer;
        
        // Assert
        Assert.NotNull(renderer);
        
        // インターフェース実装確認
        Assert.IsAssignableFrom<IOverlayRenderer>(renderer);
        Assert.IsAssignableFrom<IInPlaceTranslationOverlayManager>(renderer);
        Assert.IsAssignableFrom<IEventProcessor<OverlayUpdateEvent>>(renderer);
        
        // EventProcessor設定確認
        if (renderer is IEventProcessor<OverlayUpdateEvent> processor)
        {
            Assert.Equal(100, processor.Priority);
            Assert.False(processor.SynchronousExecution);
        }
    }
    
    [Fact]
    public void UnifiedSystem_Should_EliminateDuplicateOverlayArchitecture()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<InPlaceTranslationOverlayManager>();
        
        var phase16Module = new Phase16UIOverlayModule();
        phase16Module.RegisterServices(services);
        var serviceProvider = services.BuildServiceProvider();
        
        // Act - 各インターフェース経由でサービス取得
        var viaOverlayRenderer = serviceProvider.GetRequiredService<IOverlayRenderer>();
        var viaOverlayManager = serviceProvider.GetRequiredService<IInPlaceTranslationOverlayManager>();
        var viaEventProcessor = serviceProvider.GetRequiredService<IEventProcessor<OverlayUpdateEvent>>();
        
        // Assert - すべて同一インスタンス（重複排除確認）
        Assert.Same(viaOverlayRenderer, viaOverlayManager);
        Assert.Same(viaOverlayRenderer, viaEventProcessor);
        Assert.Same(viaOverlayManager, viaEventProcessor);
        
        // 重複オーバーレイアーキテクチャが解決されたことを確認
        Assert.Single(new object[] { viaOverlayRenderer, viaOverlayManager, viaEventProcessor }.Distinct());
    }
}