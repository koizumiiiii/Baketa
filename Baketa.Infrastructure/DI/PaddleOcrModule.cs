using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Dependency;
using Baketa.Core.Abstractions.OCR;
using Baketa.Infrastructure.OCR.PaddleOCR.Models;
using Baketa.Infrastructure.OCR.PaddleOCR.Initialization;
using Baketa.Infrastructure.OCR.PaddleOCR.Engine;
using System.IO;
using System.Net.Http;

namespace Baketa.Infrastructure.DI;

/// <summary>
/// PaddleOCR統合基盤のサービス登録モジュール（更新版）
/// </summary>
public class PaddleOcrModule : IServiceModule
{
    /// <summary>
    /// サービスを登録します
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    public void RegisterServices(IServiceCollection services)
    {
        // モデル管理基盤
        services.AddSingleton<IModelPathResolver>(serviceProvider =>
        {
            var logger = serviceProvider.GetService<ILogger<DefaultModelPathResolver>>();
            
            // デフォルトのベースディレクトリは実行ファイルのディレクトリ下のmodelsフォルダ
            var baseDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models");
            
            return new DefaultModelPathResolver(baseDirectory, logger);
        });
        
        // PaddleOCR初期化サービス
        services.AddSingleton<PaddleOcrInitializer>(serviceProvider =>
        {
            var modelPathResolver = serviceProvider.GetRequiredService<IModelPathResolver>();
            var logger = serviceProvider.GetService<ILogger<PaddleOcrInitializer>>();
            
            // デフォルトのベースディレクトリは実行ファイルのディレクトリ
            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            
            return new PaddleOcrInitializer(baseDirectory, modelPathResolver, logger);
        });
        
        // OCRモデル管理
        services.AddSingleton<IOcrModelManager>(serviceProvider =>
        {
            var modelPathResolver = serviceProvider.GetRequiredService<IModelPathResolver>();
            var logger = serviceProvider.GetService<ILogger<OcrModelManager>>();
            
            // HttpClientの取得（既存のHttpClientFactoryから、または新規作成）
            var httpClientFactory = serviceProvider.GetService<IHttpClientFactory>();
            var httpClient = httpClientFactory?.CreateClient("OcrModelDownloader") ?? new HttpClient();
            
            // 一時ディレクトリの設定
            var tempDirectory = Path.Combine(Path.GetTempPath(), "BaketaOcrModels");
            
            return new OcrModelManager(modelPathResolver, httpClient, tempDirectory, logger);
        });
        
        // OCRエンジン（IOcrEngineインターフェース準拠）
        services.AddSingleton<IOcrEngine>(serviceProvider =>
        {
            var modelPathResolver = serviceProvider.GetRequiredService<IModelPathResolver>();
            var logger = serviceProvider.GetService<ILogger<PaddleOcrEngine>>();
            
            return new PaddleOcrEngine(modelPathResolver, logger);
        });
        
        // 後方互換性のため、PaddleOcrEngineも直接登録
        services.AddSingleton<PaddleOcrEngine>(serviceProvider =>
        {
            // IOcrEngineとして登録されているインスタンスを再利用
            return (PaddleOcrEngine)serviceProvider.GetRequiredService<IOcrEngine>();
        });
        
        // HttpClient設定（HttpClientFactoryが利用可能な場合）
        if (services.Any(s => s.ServiceType == typeof(IHttpClientFactory)))
        {
            services.AddHttpClient("OcrModelDownloader", client =>
            {
                client.Timeout = TimeSpan.FromMinutes(30); // モデルダウンロード用の長いタイムアウト
                client.DefaultRequestHeaders.Add("User-Agent", "Baketa-OCR-ModelManager/1.0");
            });
        }
    }
}
