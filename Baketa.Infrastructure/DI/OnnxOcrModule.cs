using Baketa.Core.Abstractions.OCR;
using Baketa.Core.DI;
using Baketa.Infrastructure.OCR.GPU;
using Baketa.Infrastructure.OCR.ONNX;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.DI;

/// <summary>
/// ONNX OCR エンジン DIモジュール
/// Issue #181: GPU/CPU 自動切り替え対応
/// PP-OCRv5 ONNX モデルを使用した OCR エンジン登録
/// </summary>
public sealed class OnnxOcrModule : ServiceModuleBase
{
    public override void RegisterServices(IServiceCollection services)
    {
        // ONNX OCR設定登録
        RegisterSettings(services);

        // モデル設定登録
        RegisterModelConfiguration(services);

        // ONNX OCRエンジン登録
        RegisterOnnxOcrEngine(services);
    }

    private static void RegisterSettings(IServiceCollection services)
    {
        services.AddSingleton<OnnxOcrSettings>(serviceProvider =>
        {
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            return configuration.GetSection("OnnxOcr").Get<OnnxOcrSettings>() ?? new OnnxOcrSettings();
        });
    }

    private static void RegisterModelConfiguration(IServiceCollection services)
    {
        services.AddSingleton<IPpOcrv5ModelConfiguration>(serviceProvider =>
        {
            var settings = serviceProvider.GetRequiredService<OnnxOcrSettings>();
            var logger = serviceProvider.GetRequiredService<ILogger<PpOcrv5ModelConfiguration>>();

            // カスタムパスが設定されている場合はそれを使用
            if (!string.IsNullOrWhiteSpace(settings.ModelsDirectory))
            {
                return new PpOcrv5ModelConfiguration(settings.ModelsDirectory, logger);
            }

            return new PpOcrv5ModelConfiguration(logger);
        });
    }

    private static void RegisterOnnxOcrEngine(IServiceCollection services)
    {
        // ONNX OCRエンジンをSingletonとして登録
        services.AddSingleton<OnnxOcrEngine>(serviceProvider =>
        {
            var gpuOptimizer = serviceProvider.GetRequiredService<IUnifiedGpuOptimizer>();
            var modelConfig = serviceProvider.GetRequiredService<IPpOcrv5ModelConfiguration>();
            var logger = serviceProvider.GetRequiredService<ILogger<OnnxOcrEngine>>();

            return new OnnxOcrEngine(gpuOptimizer, modelConfig, logger);
        });

        // ONNX OCRエンジンをIOcrEngineとしても登録（Keyed Service）
        // 既存のPaddleOcrEngineとの共存を可能にする
        services.AddKeyedSingleton<IOcrEngine, OnnxOcrEngine>("onnx", (serviceProvider, _) =>
        {
            return serviceProvider.GetRequiredService<OnnxOcrEngine>();
        });

        // Issue #181: ONNX OCRエンジンをデフォルトIOcrEngineとして登録
        // 設定が有効かつモデルが利用可能な場合のみ、PaddleOCRの代わりにONNX OCRを使用
        // 注意: この登録はAdvancedCachingModuleより後に実行される必要がある（last-wins）
        services.AddSingleton<IOcrEngine>(serviceProvider =>
        {
            var settings = serviceProvider.GetRequiredService<OnnxOcrSettings>();
            var modelConfig = serviceProvider.GetRequiredService<IPpOcrv5ModelConfiguration>();

            // モデルが利用可能な場合のみONNX OCRを使用
            if (settings.Enabled && modelConfig.IsModelsAvailable())
            {
                var onnxEngine = serviceProvider.GetRequiredService<OnnxOcrEngine>();
                Console.WriteLine($"✅ [Issue #181] ONNX OCRエンジンをデフォルトとして使用: {onnxEngine.EngineName}");
                Console.WriteLine($"   → GPU Provider: DirectML/CUDA 自動選択有効");
                return onnxEngine;
            }

            // モデルが存在しない場合は警告ログを出力して、そのままONNXエンジンを返す
            // (初期化時にエラーハンドリングされる)
            Console.WriteLine("⚠️ [Issue #181] ONNXモデル未検出 または 無効設定");
            Console.WriteLine("   → モデルダウンロード: scripts/download-ppocrv5-models.ps1");
            Console.WriteLine("   → OnnxOcrEngineを使用（初期化時にエラーハンドリング）");

            return serviceProvider.GetRequiredService<OnnxOcrEngine>();
        });
        Console.WriteLine("✅ [Issue #181] IOcrEngine → OnnxOcrEngine 登録完了");
    }
}

/// <summary>
/// ONNX OCR 設定
/// appsettings.json "OnnxOcr" セクションで設定
/// </summary>
public sealed record OnnxOcrSettings
{
    /// <summary>
    /// ONNX OCRエンジンを有効にするか
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// ONNXモデルのルートディレクトリ
    /// 空の場合はデフォルトパスを使用
    /// </summary>
    public string ModelsDirectory { get; init; } = string.Empty;

    /// <summary>
    /// デフォルト言語
    /// </summary>
    public string DefaultLanguage { get; init; } = "jpn";

    /// <summary>
    /// GPUを優先的に使用するか
    /// </summary>
    public bool PreferGpu { get; init; } = true;

    /// <summary>
    /// CUDA使用時のデバイスID
    /// </summary>
    public int CudaDeviceId { get; init; } = 0;

    /// <summary>
    /// DirectMLを有効にするか（NVIDIA以外のGPU向け）
    /// </summary>
    public bool EnableDirectML { get; init; } = true;

    /// <summary>
    /// スレッド数（0で自動）
    /// </summary>
    public int ThreadCount { get; init; } = 0;

    /// <summary>
    /// 検出の信頼度閾値
    /// </summary>
    public double DetectionThreshold { get; init; } = 0.6;

    /// <summary>
    /// 認識の信頼度閾値
    /// </summary>
    public double RecognitionThreshold { get; init; } = 0.3;

    /// <summary>
    /// 最大検出領域数
    /// </summary>
    public int MaxDetections { get; init; } = 200;

    /// <summary>
    /// 起動時にモデルをプリロードするか
    /// </summary>
    public bool PreloadModelsOnStartup { get; init; } = true;

    /// <summary>
    /// ウォームアップを有効にするか
    /// </summary>
    public bool EnableWarmup { get; init; } = true;
}
