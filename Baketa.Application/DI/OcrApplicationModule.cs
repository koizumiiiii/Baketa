using Microsoft.Extensions.DependencyInjection;
using Baketa.Core.Abstractions.Dependency;
using Baketa.Application.Services.OCR;

namespace Baketa.Application.DI;

/// <summary>
/// OCRアプリケーションサービスのDIモジュール
/// </summary>
public class OcrApplicationModule : IServiceModule
{
    /// <summary>
    /// サービスを登録します
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    public void RegisterServices(IServiceCollection services)
    {
        // OCRアプリケーションサービス
        services.AddSingleton<IOcrApplicationService, OcrApplicationService>();
    }
}
