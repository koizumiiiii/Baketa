using System;
using Baketa.Core.Translation.Models;
using Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// SentencePieceトークナイザーのDI登録拡張メソッド
/// </summary>
public static class SentencePieceServiceCollectionExtensions
{
    /// <summary>
    /// SentencePieceトークナイザーサービスを登録
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <param name="configuration">設定</param>
    /// <returns>サービスコレクション</returns>
    public static IServiceCollection AddSentencePieceTokenizer(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 設定の登録
        services.Configure<SentencePieceOptions>(
            configuration.GetSection("SentencePiece"));

        // モデルマネージャーの登録
        services.AddSingleton<SentencePieceModelManager>();

        // HTTPクライアントファクトリーの登録（まだ登録されていない場合）
        services.AddHttpClient();

        // トークナイザーの登録
        services.AddSingleton<ITokenizer>(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<SentencePieceOptions>>();
            var modelManager = serviceProvider.GetRequiredService<SentencePieceModelManager>();
            var logger = serviceProvider.GetRequiredService<ILogger<RealSentencePieceTokenizer>>();

            // デフォルトモデルのパスを取得（同期的に実行）
            var modelPath = modelManager.GetModelPathAsync(options.Value.DefaultModel)
                .GetAwaiter()
                .GetResult();

            return new RealSentencePieceTokenizer(
                modelPath,
                logger,
                options.Value.MaxInputLength);
        });

        return services;
    }

    /// <summary>
    /// SentencePieceトークナイザーサービスを登録（カスタム設定）
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <param name="configureOptions">オプション設定アクション</param>
    /// <returns>サービスコレクション</returns>
    public static IServiceCollection AddSentencePieceTokenizer(
        this IServiceCollection services,
        Action<SentencePieceOptions> configureOptions)
    {
        // 設定の登録
        services.Configure(configureOptions);

        // モデルマネージャーの登録
        services.AddSingleton<SentencePieceModelManager>();

        // HTTPクライアントファクトリーの登録（まだ登録されていない場合）
        services.AddHttpClient();

        // トークナイザーの登録
        services.AddSingleton<ITokenizer>(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<SentencePieceOptions>>();
            var modelManager = serviceProvider.GetRequiredService<SentencePieceModelManager>();
            var logger = serviceProvider.GetRequiredService<ILogger<RealSentencePieceTokenizer>>();

            // デフォルトモデルのパスを取得（同期的に実行）
            var modelPath = modelManager.GetModelPathAsync(options.Value.DefaultModel)
                .GetAwaiter()
                .GetResult();

            return new RealSentencePieceTokenizer(
                modelPath,
                logger,
                options.Value.MaxInputLength);
        });

        return services;
    }

    /// <summary>
    /// 名前付きSentencePieceトークナイザーサービスを登録
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <param name="name">トークナイザー名</param>
    /// <param name="modelName">モデル名</param>
    /// <param name="configuration">設定</param>
    /// <returns>サービスコレクション</returns>
    public static IServiceCollection AddNamedSentencePieceTokenizer(
        this IServiceCollection services,
        string name,
        string modelName,
        IConfiguration configuration)
    {
        // 設定の登録（まだ登録されていない場合）
        services.Configure<SentencePieceOptions>(
            configuration.GetSection("SentencePiece"));

        // モデルマネージャーの登録（まだ登録されていない場合）
        services.TryAddSingleton<SentencePieceModelManager>();

        // HTTPクライアントファクトリーの登録（まだ登録されていない場合）
        services.AddHttpClient();

        // 名前付きトークナイザーの登録
        services.AddKeyedSingleton<ITokenizer>(name, (serviceProvider, _) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<SentencePieceOptions>>();
            var modelManager = serviceProvider.GetRequiredService<SentencePieceModelManager>();
            var logger = serviceProvider.GetRequiredService<ILogger<RealSentencePieceTokenizer>>();

            // 指定されたモデルのパスを取得（同期的に実行）
            var modelPath = modelManager.GetModelPathAsync(modelName)
                .GetAwaiter()
                .GetResult();

            return new RealSentencePieceTokenizer(
                modelPath,
                logger,
                options.Value.MaxInputLength);
        });

        return services;
    }
}

/// <summary>
/// ServiceCollectionの拡張メソッド用ヘルパー
/// </summary>
internal static class ServiceCollectionExtensions
{
    /// <summary>
    /// サービスがまだ登録されていない場合のみ登録
    /// </summary>
    internal static IServiceCollection TryAddSingleton<TService, TImplementation>(
        this IServiceCollection services)
        where TService : class
        where TImplementation : class, TService
    {
        if (!services.Any(d => d.ServiceType == typeof(TService)))
        {
            services.AddSingleton<TService, TImplementation>();
        }
        return services;
    }

    /// <summary>
    /// サービスがまだ登録されていない場合のみ登録
    /// </summary>
    internal static IServiceCollection TryAddSingleton<TService>(
        this IServiceCollection services)
        where TService : class
    {
        if (!services.Any(d => d.ServiceType == typeof(TService)))
        {
            services.AddSingleton<TService>();
        }
        return services;
    }
}
