using Baketa.Core.DI;
using Baketa.Core.Translation.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation.Local.Onnx;

/// <summary>
/// αテスト向けOPUS-MT翻訳サービスのDIモジュール
/// </summary>
public class AlphaOpusMtModule : ServiceModuleBase
{
    /// <inheritdoc/>
    public override void RegisterServices(IServiceCollection services)
    {
        // 設定を登録
        services.AddSingleton<AlphaOpusMtConfiguration>();

        // ファクトリーを登録
        services.AddSingleton<AlphaOpusMtEngineFactory>();

        // αテスト向け翻訳サービスを登録
        services.AddSingleton<AlphaOpusMtTranslationService>();

        // αテスト向けなので、ITranslationServiceとしての登録はスキップ
    }
}