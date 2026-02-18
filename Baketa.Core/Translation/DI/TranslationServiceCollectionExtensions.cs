using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Translation.Abstractions;
using Baketa.Core.Translation.Cache;
using Baketa.Core.Translation.Common;
using Baketa.Core.Translation.Events;
using Baketa.Core.Translation.Repositories;
using Baketa.Core.Translation.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
// 名前空間エイリアスを使用して競合を解決
using CoreTranslationService = Baketa.Core.Abstractions.Translation.ITranslationService;
using TranslationAbstractionService = Baketa.Core.Translation.Abstractions.ITranslationService;

namespace Baketa.Core.Translation.DI;

/// <summary>
/// 翻訳サービスのDI拡張メソッド
/// </summary>
public static class TranslationServiceCollectionExtensions
{
    /// <summary>
    /// 翻訳サービスをDIコンテナに登録します
    /// </summary>
    /// <param name="services">DIサービスコレクション</param>
    /// <param name="options">翻訳オプション</param>
    /// <returns>DIサービスコレクション</returns>
    public static IServiceCollection AddTranslationServices(
        this IServiceCollection services,
        TranslationOptions? options = null)
    {
        // デフォルトのオプション
        options ??= new TranslationOptions();

        // 基本サービス登録
        services.AddSingleton(options);

        // 各コンポーネントの登録
        services.AddTranslationEngines(options.WebApiOptions);
        services.AddTranslationManagement(options.ManagementOptions);
        services.AddTranslationCache(options.CacheOptions);
        services.AddTranslationEvents(options.EventOptions);
        services.AddTranslationPipeline(options);

        return services;
    }

    /// <summary>
    /// 翻訳エンジンをDIコンテナに登録します
    /// </summary>
    /// <param name="services">DIサービスコレクション</param>
    /// <param name="options">WebAPI翻訳オプション</param>
    /// <returns>DIサービスコレクション</returns>
    public static IServiceCollection AddTranslationEngines(
        this IServiceCollection services,
        WebApiTranslationOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        // HttpClientを登録
        services.AddHttpClient();

        // プロダクション用翻訳エンジン
        // Local: NLLB-200翻訳エンジン
        // Gemini: Google Gemini AI翻訳エンジン

        // 注意: DummyEngineとSimpleEngineはテスト用のため、プロダクションコードでは使用しない
        // テスト時は Baketa.Core.Tests.Translation.Testing 名前空間から利用可能

        return services;
    }

    /// <summary>
    /// 翻訳結果管理をDIコンテナに登録します
    /// </summary>
    /// <param name="services">DIサービスコレクション</param>
    /// <param name="options">翻訳結果管理オプション</param>
    /// <returns>DIサービスコレクション</returns>
    public static IServiceCollection AddTranslationManagement(
        this IServiceCollection services,
        TranslationManagementOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        // 翻訳結果管理サービスの登録
        services.AddSingleton<ITranslationManager, InMemoryTranslationManager>();
        services.AddSingleton<ITranslationRepository, InMemoryTranslationRepository>();

        return services;
    }

    /// <summary>
    /// 翻訳キャッシュをDIコンテナに登録します
    /// </summary>
    /// <param name="services">DIサービスコレクション</param>
    /// <param name="options">翻訳キャッシュオプション</param>
    /// <returns>DIサービスコレクション</returns>
    public static IServiceCollection AddTranslationCache(
        this IServiceCollection services,
        TranslationCacheOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        // 翻訳キャッシュの登録
        if (options.EnableMemoryCache)
        {
            services.AddSingleton<ITranslationCache, MemoryTranslationCache>();
        }

        if (options.EnablePersistentCache)
        {
            // 引数の確認のために仮実装を使用
            services.AddSingleton<ITranslationPersistentCache, DummyPersistentCache>();
        }

        return services;
    }

    /// <summary>
    /// 翻訳パイプラインをDIコンテナに登録します
    /// </summary>
    /// <param name="services">DIサービスコレクション</param>
    /// <param name="options">翻訳オプション</param>
    /// <returns>DIサービスコレクション</returns>
    public static IServiceCollection AddTranslationPipeline(
        this IServiceCollection services,
        TranslationOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        // パイプラインとトランザクションマネージャーは別プロジェクトで実装されるため、インターフェースのみ登録
        services.AddSingleton<ITranslationEngineDiscovery, DefaultTranslationEngineDiscovery>();

        // 型の依存関係だけ定義
        services.AddSingleton<ITranslationTransactionManager>(provider => null!);
        services.AddSingleton<ITranslationPipeline>(provider => null!);
        services.AddSingleton<CoreTranslationService>(provider => null!);

        return services;
    }

    /// <summary>
    /// 翻訳イベントをDIコンテナに登録します
    /// </summary>
    /// <param name="services">DIサービスコレクション</param>
    /// <param name="options">翻訳イベントオプション</param>
    /// <returns>DIサービスコレクション</returns>
    public static IServiceCollection AddTranslationEvents(
        this IServiceCollection services,
        TranslationEventOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        // 基本的なイベント型の登録
        if (options.EnableEvents)
        {
            // メインのEventAggregator（Baketa.Core.Events.Implementation.EventAggregator）を使用
            // 重複するDefaultEventAggregatorの登録は削除して統一

            // イベントハンドラーを登録
            services.AddTransient<ITranslationEventHandler<TranslationStartedEvent>, LoggingTranslationEventHandler>();
            services.AddTransient<ITranslationEventHandler<TranslationCompletedEvent>, LoggingTranslationEventHandler>();
            services.AddTransient<ITranslationEventHandler<TranslationErrorEvent>, LoggingTranslationEventHandler>();
        }

        return services;
    }
}
