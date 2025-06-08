using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Dependency;
using Baketa.Infrastructure.OCR.PaddleOCR.Models;
using Baketa.Infrastructure.OCR.PaddleOCR.Initialization;
using Baketa.Infrastructure.OCR.PaddleOCR.Engine;

namespace Baketa.Infrastructure.DI;

/// <summary>
/// PaddleOCR統合基盤のサービス登録モジュール
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
            
            // デフォルトのベースディレクトリは実行ファイルのディレクトリ
            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            
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
        
        // PaddleOCRエンジン
        services.AddSingleton<PaddleOcrEngine>(serviceProvider =>
        {
            var modelPathResolver = serviceProvider.GetRequiredService<IModelPathResolver>();
            var logger = serviceProvider.GetService<ILogger<PaddleOcrEngine>>();
            
            return new PaddleOcrEngine(modelPathResolver, logger);
        });
    }
}
