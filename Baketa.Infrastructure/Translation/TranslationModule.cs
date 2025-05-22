using System;
using System.Net.Http;
using System.Net.Http.Headers;
using Baketa.Core.Abstractions.DI;
using Baketa.Core.Translation.Abstractions;
using Baketa.Core.Translation.Models;
using Baketa.Infrastructure.Translation.Cloud;
using Baketa.Infrastructure.Translation.Local;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Infrastructure.Translation;

/// <summary>
/// 翻訳サービスのDI登録モジュール
/// </summary>
public class TranslationModule : IServiceModule
{
    // APIエンドポイント定数
    private const string DEFAULT_API_ENDPOINT = "https://generativelanguage.googleapis.com/v1/models/";
    
    /// <summary>
    /// サービスの登録
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    public void RegisterServices(IServiceCollection services)
    {
        // 基本サービス登録
        services.AddOptions();
        services.AddHttpClient();
        
        // 翻訳エンジンの設定
        RegisterTranslationEngines(services);
        
        // トークナイザーとモデルローダーの登録
        RegisterTokenizers(services);
        RegisterModelLoaders(services);
    }
    
    /// <summary>
    /// 翻訳エンジンの登録
    /// </summary>
    private void RegisterTranslationEngines(IServiceCollection services)
    {
        // Geminiクラウド翻訳エンジン
        services.Configure<GeminiEngineOptions>(options =>
        {
            // オプション設定はappsettingsから注入される前提
            options.TimeoutSeconds = 30;
            options.RetryCount = 3;
        });
        
        services.AddTransient<ICloudTranslationEngine>(sp =>
        {
            // HttpClientを直接生成します
            var httpClient = new HttpClient();
            
            // GeminiEngineのオプション取得
            var options = sp.GetRequiredService<IOptions<GeminiEngineOptions>>();
            var engineOptions = options.Value;
            
            var baseUrl = engineOptions.ApiEndpoint ?? DEFAULT_API_ENDPOINT;
            if (!baseUrl.EndsWith('/'))
            {
                baseUrl += '/';
            }
            
            httpClient.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
            httpClient.DefaultRequestHeaders.Accept.Clear();
            httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
                
            var logger = sp.GetRequiredService<ILogger<GeminiTranslationEngine>>();
            
            return new GeminiTranslationEngine(httpClient, options, logger);
        });
        
        // ONNX翻訳エンジンは実装時に登録
        // 以下はファクトリー登録の例
        services.AddTransient<Func<string, ILocalTranslationEngine>>(sp => modelPath =>
        {
            var options = new OnnxTranslationOptions
            {
                MaxSequenceLength = 512,
                ThreadCount = Environment.ProcessorCount / 2, // 半分のコアを使用
                EnableModelCache = true
            };
            
            var modelLoader = sp.GetRequiredService<IModelLoader>();
            var tokenizer = sp.GetRequiredService<ITokenizer>();
            var logger = sp.GetRequiredService<ILogger<OnnxTranslationEngine>>();
            
            // 言語ペアはモデルロード時に決定される想定
            var languagePair = new LanguagePair
            {
                SourceLanguage = new Language { Code = "en", DisplayName = "English" },
                TargetLanguage = new Language { Code = "ja", DisplayName = "Japanese" }
            };
            
            return new OnnxTranslationEngine(
                modelPath,
                languagePair,
                modelLoader,
                tokenizer,
                options,
                logger);
        });
    }
    
    /// <summary>
    /// トークナイザーの登録
    /// </summary>
    private void RegisterTokenizers(IServiceCollection services)
    {
        // ダミーのものを仮登録
        services.AddSingleton<ITokenizer>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<DummyTokenizer>>();
            return new DummyTokenizer(logger);
        });
    }
    
    /// <summary>
    /// モデルローダーの登録
    /// </summary>
    private void RegisterModelLoaders(IServiceCollection services)
    {
        // ダミーのものを仮登録
        services.AddSingleton<IModelLoader>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<DummyModelLoader>>();
            return new DummyModelLoader(logger);
        });
    }
}

/// <summary>
/// ダミーのトークナイザー実装（実際の実装作成時に置き換え）
/// </summary>
internal class DummyTokenizer : ITokenizer
{
    private readonly ILogger<DummyTokenizer> _logger;
    
    public string TokenizerId => "dummy-tokenizer";
    public string Name => "Dummy Tokenizer";
    public int VocabularySize => 10000;
    
    public DummyTokenizer(ILogger<DummyTokenizer> logger)
    {
        _logger = logger;
    }
    
    public int[] Tokenize(string text)
    {
        _logger.LogInformation("ダミートークナイザー: テキストをトークン化します");
        // 実際の実装では、適切なトークナイズロジックを実装
        return [];
    }
    
    public string Decode(int[] tokens)
    {
        _logger.LogInformation("ダミートークナイザー: トークンをデコードします");
        // 実際の実装では、適切なデコードロジックを実装
        return string.Empty;
    }
    
    public string DecodeToken(int token)
    {
        _logger.LogInformation("ダミートークナイザー: 単一トークンをデコードします");
        // 実際の実装では、適切なデコードロジックを実装
        return string.Empty;
    }
}

/// <summary>
/// ダミーのモデルローダー実装（実際の実装作成時に置き換え）
/// </summary>
internal class DummyModelLoader : IModelLoader
{
    private readonly ILogger<DummyModelLoader> _logger;
    
    public DummyModelLoader(ILogger<DummyModelLoader> logger)
    {
        _logger = logger;
    }
    
    public Task<bool> LoadModelAsync(string modelPath, ModelOptions? options = null)
    {
        _logger.LogInformation("ダミーモデルローダー: モデルをロードします: {ModelPath}", modelPath);
        return Task.FromResult(true);
    }
    
    public bool IsModelLoaded()
    {
        return true;
    }
    
    public Task<bool> UnloadModelAsync()
    {
        _logger.LogInformation("ダミーモデルローダー: モデルをアンロードします");
        return Task.FromResult(true);
    }
    
    public Task<IReadOnlyList<ComputeDevice>> GetAvailableDevicesAsync()
    {
        var devices = new List<ComputeDevice>
        {
            ComputeDevice.DefaultCpu
        };
        
        return Task.FromResult<IReadOnlyList<ComputeDevice>>(devices);
    }
    
    public Task<bool> SetDeviceAsync(ComputeDevice device)
    {
        _logger.LogInformation("ダミーモデルローダー: デバイスを設定します: {DeviceId}", device.DeviceId);
        return Task.FromResult(true);
    }
    
    public ComputeDevice GetCurrentDevice()
    {
        return ComputeDevice.DefaultCpu;
    }
}