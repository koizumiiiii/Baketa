using System;
using Baketa.Application.Services.Translation;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Events.EventTypes;
using FluentAssertions;
using Baketa.Core.Abstractions.Translation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Baketa.Application.Tests.Services.Translation;

/// <summary>
/// Phase 4 Migration 検証テスト
/// OcrCompletedHandler_Improved → TranslationPipelineService 移行の基本検証
/// </summary>
public class TranslationPipelineMigrationTests
{
    /// <summary>
    /// Phase 4 Migration: DI登録検証
    /// TranslationPipelineService が IEventProcessor<OcrCompletedEvent> として正しく登録されることを確認
    /// </summary>
    [Fact]
    public void Migration_DIRegistration_ShouldResolveTranslationPipelineService()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        
        // Mock dependencies
        services.AddSingleton(Mock.Of<IEventAggregator>());
        services.AddSingleton(Mock.Of<Baketa.Core.Abstractions.Settings.IUnifiedSettingsService>());
        services.AddSingleton(Mock.Of<Baketa.Core.Abstractions.Translation.ITranslationService>());
        services.AddSingleton(Mock.Of<Baketa.Core.Abstractions.UI.IInPlaceTranslationOverlayManager>());
        
        // Phase 4 Migration DI configuration
        services.AddSingleton<TranslationPipelineService>();
        services.AddSingleton<IEventProcessor<OcrCompletedEvent>>(
            provider => provider.GetRequiredService<TranslationPipelineService>());

        var serviceProvider = services.BuildServiceProvider();

        // Act
        var eventProcessor = serviceProvider.GetService<IEventProcessor<OcrCompletedEvent>>();
        var pipelineService = serviceProvider.GetService<TranslationPipelineService>();

        // Assert
        eventProcessor.Should().NotBeNull("IEventProcessor<OcrCompletedEvent> should be registered");
        pipelineService.Should().NotBeNull("TranslationPipelineService should be registered");
        eventProcessor.Should().BeSameAs(pipelineService, "IEventProcessor should resolve to TranslationPipelineService");
        
        // Cleanup
        serviceProvider.Dispose();
    }

    /// <summary>
    /// TranslationPipelineService インスタンス作成検証
    /// </summary>
    [Fact]
    public void TranslationPipelineService_Constructor_ShouldCreateValidInstance()
    {
        // Arrange
        var mockEventAggregator = Mock.Of<IEventAggregator>();
        var mockSettingsService = Mock.Of<Baketa.Core.Abstractions.Settings.IUnifiedSettingsService>();
        var mockTranslationService = Mock.Of<Baketa.Core.Abstractions.Translation.ITranslationService>();
        var mockOverlayManager = Mock.Of<Baketa.Core.Abstractions.UI.IInPlaceTranslationOverlayManager>();
        var mockLogger = Mock.Of<ILogger<TranslationPipelineService>>();
        var mockLanguageConfig = Mock.Of<ILanguageConfigurationService>();

        // Act
        var service = new TranslationPipelineService(
            mockEventAggregator,
            mockSettingsService,
            mockTranslationService,
            mockOverlayManager,
            mockLogger,
            mockLanguageConfig);

        // Assert
        service.Should().NotBeNull();
        service.Priority.Should().Be(0);
        service.SynchronousExecution.Should().BeFalse();
        
        // Cleanup
        service.Dispose();
    }

    /// <summary>
    /// Phase 4 Migration検証: OcrCompletedHandler_Improved が登録されていないことを確認
    /// </summary>
    [Fact]
    public void Migration_OcrCompletedHandler_Improved_ShouldNotBeRegistered()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        
        // Mock dependencies (without OcrCompletedHandler_Improved)
        services.AddSingleton(Mock.Of<IEventAggregator>());
        services.AddSingleton(Mock.Of<Baketa.Core.Abstractions.Settings.IUnifiedSettingsService>());
        services.AddSingleton(Mock.Of<Baketa.Core.Abstractions.Translation.ITranslationService>());
        
        // Phase 4 Migration: Only TranslationPipelineService
        services.AddSingleton<TranslationPipelineService>();
        services.AddSingleton<IEventProcessor<OcrCompletedEvent>>(
            provider => provider.GetRequiredService<TranslationPipelineService>());

        var serviceProvider = services.BuildServiceProvider();

        // Act & Assert
        // OcrCompletedHandler_Improved should not be resolvable
        var act = () => serviceProvider.GetRequiredService<Baketa.Core.Events.Handlers.OcrCompletedHandler_Improved>();
        act.Should().Throw<InvalidOperationException>("OcrCompletedHandler_Improved should not be registered");
        
        // But TranslationPipelineService should be resolvable
        var pipelineService = serviceProvider.GetService<TranslationPipelineService>();
        pipelineService.Should().NotBeNull("TranslationPipelineService should be available");
        
        // Cleanup
        serviceProvider.Dispose();
    }
}