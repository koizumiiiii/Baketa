using System;
using System.Net.Http;
using System.Net.Http.Headers;
using Baketa.Core.Translation.Abstractions;
using Baketa.Infrastructure.Translation.Cloud;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace Baketa.Infrastructure.Translation.Extensions;

/// <summary>
/// Gemini翻訳サービスのDI拡張メソッド
/// </summary>
public static class GeminiTranslationServiceExtensions
{
    /// <summary>
    /// Gemini翻訳サービスを登録します
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <param name="configuration">設定</param>
    /// <returns>サービスコレクション</returns>
    public static IServiceCollection AddGeminiTranslation(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        // GeminiEngineOptionsの設定
        services.Configure<GeminiEngineOptions>(
            configuration.GetSection("GeminiApi"));

        // 設定検証
        services.AddOptions<GeminiEngineOptions>()
            .Bind(configuration.GetSection("GeminiApi"))
            .Validate(options =>
            {
                return !string.IsNullOrWhiteSpace(options.ApiKey);
            }, "Gemini APIキーが設定されていません");

        // HttpClientFactoryを使用したHTTPクライアント登録
        services.AddHttpClient<GeminiTranslationEngine>("GeminiClient", (sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<GeminiEngineOptions>>().Value;
            
            var baseUrl = options.ApiEndpoint ?? "https://generativelanguage.googleapis.com/v1/models/";
#pragma warning disable CA1865 // EndsWith メソッドには char 引数のオーバーロードが存在しないため
            if (!baseUrl.EndsWith("/", StringComparison.Ordinal))
#pragma warning restore CA1865
            {
                baseUrl += "/";
            }
            
            client.BaseAddress = new Uri(baseUrl);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("Baketa", "1.0"));
            
            // タイムアウト設定
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        })
        .ConfigureAdditionalHttpMessageHandlers((handlers, _) =>
        {
            // リトライポリシーの設定
            handlers.Add(new RetryHandler());
        });

        // Gemini翻訳エンジンの登録
        services.AddTransient<ICloudTranslationEngine, GeminiTranslationEngine>();
        services.AddTransient<GeminiTranslationEngine>();

        return services;
    }

    /// <summary>
    /// Gemini翻訳サービスを詳細設定で登録します
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <param name="configureOptions">オプション設定アクション</param>
    /// <returns>サービスコレクション</returns>
    public static IServiceCollection AddGeminiTranslation(
        this IServiceCollection services,
        Action<GeminiEngineOptions> configureOptions)
    {
        services.Configure(configureOptions);

        // 設定検証
        services.AddOptions<GeminiEngineOptions>()
            .Configure(configureOptions)
            .Validate(options =>
            {
                return !string.IsNullOrWhiteSpace(options.ApiKey);
            }, "Gemini APIキーが設定されていません");

        // HttpClientFactoryを使用したHTTPクライアント登録
        services.AddHttpClient<GeminiTranslationEngine>("GeminiClient", (sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<GeminiEngineOptions>>().Value;
            
            var baseUrl = options.ApiEndpoint ?? "https://generativelanguage.googleapis.com/v1/models/";
#pragma warning disable CA1865 // EndsWith メソッドには char 引数のオーバーロードが存在しないため
            if (!baseUrl.EndsWith("/", StringComparison.Ordinal))
#pragma warning restore CA1865
            {
                baseUrl += "/";
            }
            
            client.BaseAddress = new Uri(baseUrl);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("Baketa", "1.0"));
            
            // タイムアウト設定
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        })
        .ConfigureAdditionalHttpMessageHandlers((handlers, _) =>
        {
            // リトライポリシーの設定
            handlers.Add(new RetryHandler());
        });

        // Gemini翻訳エンジンの登録
        services.AddTransient<ICloudTranslationEngine, GeminiTranslationEngine>();
        services.AddTransient<GeminiTranslationEngine>();

        return services;
    }
}

/// <summary>
/// HTTPリトライハンドラー
/// </summary>
internal sealed class RetryHandler : DelegatingHandler
{
    private const int MaxRetries = 3;
    private static readonly TimeSpan[] RetryDelays = 
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4)
    ];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, 
        CancellationToken cancellationToken)
    {
        HttpResponseMessage? response = null;
        
        for (int i = 0; i <= MaxRetries; i++)
        {
            try
            {
                response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
                
                // 成功またはリトライ不要な場合は即座に返す
                if (response.IsSuccessStatusCode || !ShouldRetry(response))
                {
                    return response;
                }
                
                // 最後の試行でない場合は待機
                if (i < MaxRetries)
                {
                    await Task.Delay(RetryDelays[i], cancellationToken).ConfigureAwait(false);
                }
            }
            catch (HttpRequestException) when (i < MaxRetries)
            {
                // HTTPリクエスト例外は最後の試行以外では無視
                if (i < MaxRetries)
                {
                    await Task.Delay(RetryDelays[i], cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    throw;
                }
            }
            catch (TaskCanceledException) when (i < MaxRetries && !cancellationToken.IsCancellationRequested)
            {
                // タイムアウトは最後の試行以外では無視
                if (i < MaxRetries)
                {
                    await Task.Delay(RetryDelays[i], cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    throw;
                }
            }
        }
        
        return response ?? throw new InvalidOperationException("レスポンスが取得できませんでした");
    }
    
    /// <summary>
    /// リトライすべきかどうかを判定
    /// </summary>
    private static bool ShouldRetry(HttpResponseMessage response)
    {
        return response.StatusCode is 
            System.Net.HttpStatusCode.RequestTimeout or
            System.Net.HttpStatusCode.TooManyRequests or
            System.Net.HttpStatusCode.InternalServerError or
            System.Net.HttpStatusCode.BadGateway or
            System.Net.HttpStatusCode.ServiceUnavailable or
            System.Net.HttpStatusCode.GatewayTimeout;
    }
}