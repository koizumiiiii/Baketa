using System;
using Baketa.Infrastructure.Translation.Local.Onnx.Chinese;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Baketa.Infrastructure.Translation.Local.Onnx.Chinese;

/// <summary>
/// 中国語翻訳サポートのDI拡張メソッド
/// </summary>
public static class ChineseTranslationServiceCollectionExtensions
{
    /// <summary>
    /// 中国語翻訳サポートをDIコンテナに追加
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <param name="configuration">設定</param>
    /// <returns>拡張されたサービスコレクション</returns>
    public static IServiceCollection AddChineseTranslationSupport(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // 中国語言語処理器の登録
        services.AddSingleton<ChineseLanguageProcessor>();

        // 中国語変種検出サービスの登録
        services.AddSingleton<ChineseVariantDetectionService>();

        // 設定に応じて中国語翻訳エンジンも登録可能
        // （実際の実装では OpusMtOnnxEngine が必要）

        return services;
    }

    /// <summary>
    /// 中国語翻訳サポートをDIコンテナに追加（オプション設定付き）
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <param name="configureOptions">オプション設定アクション</param>
    /// <returns>拡張されたサービスコレクション</returns>
    public static IServiceCollection AddChineseTranslationSupport(
        this IServiceCollection services,
        Action<ChineseTranslationOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // 中国語言語処理器の登録
        services.AddSingleton<ChineseLanguageProcessor>();

        // 中国語変種検出サービスの登録
        services.AddSingleton<ChineseVariantDetectionService>();

        // オプション設定があれば適用
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }

        return services;
    }
}

/// <summary>
/// 中国語翻訳のオプション設定
/// </summary>
public class ChineseTranslationOptions
{
    /// <summary>
    /// 自動変種検出を有効にするかどうか
    /// </summary>
    public bool EnableAutoVariantDetection { get; set; } = true;

    /// <summary>
    /// デフォルトの中国語変種
    /// </summary>
    public Core.Translation.Models.ChineseVariant DefaultVariant { get; set; } = Core.Translation.Models.ChineseVariant.Auto;

    /// <summary>
    /// OPUS-MTプレフィックスの自動付与を有効にするかどうか
    /// </summary>
    public bool EnableOpusPrefixInsertion { get; set; } = true;

    /// <summary>
    /// 検出の信頼度しきい値
    /// </summary>
    public double ConfidenceThreshold { get; set; } = 0.7;
}
