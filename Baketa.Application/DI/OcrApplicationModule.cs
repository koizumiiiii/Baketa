using Baketa.Application.Services.OCR;
using Baketa.Core.Abstractions.Dependency;
using Microsoft.Extensions.DependencyInjection;

namespace Baketa.Application.DI;

/// <summary>
/// OCRアプリケーションサービスのDIモジュール
/// </summary>
public sealed class OcrApplicationModule : IServiceModule
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
