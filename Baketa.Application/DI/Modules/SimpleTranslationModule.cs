using Baketa.Application.Services.ErrorHandling;
using Baketa.Application.Services.Memory;
using Baketa.Application.Services.Translation;
using Baketa.Core.Abstractions.ErrorHandling;
using Baketa.Core.Abstractions.Memory;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.DI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Baketa.Application.DI.Modules;

/// <summary>
/// Simple Translation Architecture用のDIモジュール
/// Phase 2で実装した新しいCore層インターフェースとApplication層実装を登録
/// </summary>
public sealed class SimpleTranslationModule : ServiceModuleBase
{
    /// <summary>
    /// サービス登録の実装
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    public override void RegisterServices(IServiceCollection services)
    {
        // Core層インターフェースの実装を登録
        // Note: ISafeImageFactory/IImageLifecycleManagerはApplicationModuleで統合登録済み
        services.AddSingleton<ISimpleErrorHandler, SimpleErrorHandler>();

        // 設計意図：ISimpleTranslationServiceはScopedライフタイムで登録
        // - デスクトップアプリケーションでは、特定の翻訳セッション（ウィンドウ単位）でのスコープを想定
        // - IDisposableなサービスなので、スコープ終了時に自動的にDisposeされる
        // - 複数の並行翻訳処理の分離とリソース管理の観点からScopedが適切
        services.AddScoped<ISimpleTranslationService, SimpleTranslationService>();

        LogRegistrationInfo("SimpleTranslationModule services registered successfully");
    }

    /// <summary>
    /// モジュール依存関係の定義
    /// </summary>
    /// <returns>依存するモジュール型のリスト</returns>
    public override IEnumerable<Type> GetDependentModules()
    {
        // 既存の基本サービスモジュールに依存
        return
        [
            // 基本的なCaptureService, OcrService, TranslationServiceが必要
            // typeof(CaptureModule),
            // typeof(ApplicationModule)
        ];
    }

    private void LogRegistrationInfo(string message)
    {
        // Console.WriteLine($"[SimpleTranslationModule] {message}");
        System.Diagnostics.Debug.WriteLine($"[SimpleTranslationModule] {message}");
    }

    private void LogValidationInfo(string message)
    {
        // Console.WriteLine($"[SimpleTranslationModule] Validation: {message}");
        System.Diagnostics.Debug.WriteLine($"[SimpleTranslationModule] Validation: {message}");
    }

    private void LogValidationError(string message)
    {
        // Console.WriteLine($"[SimpleTranslationModule] ERROR: {message}");
        System.Diagnostics.Debug.WriteLine($"[SimpleTranslationModule] ERROR: {message}");
    }
}