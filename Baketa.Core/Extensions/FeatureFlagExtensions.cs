using Baketa.Core.Abstractions.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace Baketa.Core.Extensions;

/// <summary>
/// フィーチャーフラグ関連の拡張メソッド
/// </summary>
public static class FeatureFlagExtensions
{
    /// <summary>
    /// フィーチャーフラグが有効な場合のみアクションを実行
    /// </summary>
    /// <param name="featureFlagService">フィーチャーフラグサービス</param>
    /// <param name="featureName">機能名</param>
    /// <param name="action">実行するアクション</param>
    public static void ExecuteIfEnabled(this IFeatureFlagService featureFlagService, string featureName, Action action)
    {
        if (featureFlagService.IsFeatureEnabled(featureName))
        {
            action();
        }
    }

    /// <summary>
    /// フィーチャーフラグが有効な場合のみ非同期アクションを実行
    /// </summary>
    /// <param name="featureFlagService">フィーチャーフラグサービス</param>
    /// <param name="featureName">機能名</param>
    /// <param name="asyncAction">実行する非同期アクション</param>
    public static async Task ExecuteIfEnabledAsync(this IFeatureFlagService featureFlagService, string featureName, Func<Task> asyncAction)
    {
        if (featureFlagService.IsFeatureEnabled(featureName))
        {
            await asyncAction().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// フィーチャーフラグが有効な場合とそうでない場合で異なる値を返す
    /// </summary>
    /// <typeparam name="T">戻り値の型</typeparam>
    /// <param name="featureFlagService">フィーチャーフラグサービス</param>
    /// <param name="featureName">機能名</param>
    /// <param name="enabledValue">有効時の値</param>
    /// <param name="disabledValue">無効時の値</param>
    /// <returns>フィーチャーフラグの状態に応じた値</returns>
    public static T GetValueByFlag<T>(this IFeatureFlagService featureFlagService, string featureName, T enabledValue, T disabledValue)
    {
        return featureFlagService.IsFeatureEnabled(featureName) ? enabledValue : disabledValue;
    }

    /// <summary>
    /// サービスコレクションでフィーチャーフラグを使った条件付きサービス登録
    /// </summary>
    /// <typeparam name="TInterface">インターフェース型</typeparam>
    /// <typeparam name="TImplementation">実装型</typeparam>
    /// <param name="services">サービスコレクション</param>
    /// <param name="featureFlagService">フィーチャーフラグサービス</param>
    /// <param name="featureName">機能名</param>
    /// <param name="fallbackImplementation">フォールバック実装のファクトリ（オプション）</param>
    /// <returns>サービスコレクション</returns>
    public static IServiceCollection AddConditionalService<TInterface, TImplementation>(
        this IServiceCollection services,
        IFeatureFlagService featureFlagService,
        string featureName,
        Func<IServiceProvider, TInterface>? fallbackImplementation = null)
        where TInterface : class
        where TImplementation : class, TInterface
    {
        if (featureFlagService.IsFeatureEnabled(featureName))
        {
            services.AddScoped<TInterface, TImplementation>();
        }
        else if (fallbackImplementation != null)
        {
            services.AddScoped(fallbackImplementation);
        }

        return services;
    }

    /// <summary>
    /// アルファテスト用の機能制限チェック
    /// </summary>
    /// <param name="featureFlagService">フィーチャーフラグサービス</param>
    /// <returns>アルファテスト制限が適用されている場合true</returns>
    public static bool IsAlphaTestRestricted(this IFeatureFlagService featureFlagService)
    {
        return !featureFlagService.IsAuthenticationEnabled
            && !featureFlagService.IsCloudTranslationEnabled
            && !featureFlagService.IsAdvancedUIEnabled
            && !featureFlagService.IsChineseOCREnabled;
    }

    /// <summary>
    /// 本番環境準備チェック
    /// </summary>
    /// <param name="featureFlagService">フィーチャーフラグサービス</param>
    /// <returns>本番環境に必要な機能が有効な場合true</returns>
    public static bool IsProductionReady(this IFeatureFlagService featureFlagService)
    {
        return featureFlagService.IsAuthenticationEnabled
            && featureFlagService.IsCloudTranslationEnabled
            && featureFlagService.IsAutoUpdateEnabled
            && featureFlagService.IsFeedbackEnabled;
    }

    /// <summary>
    /// デバッグ機能の利用可能性チェック
    /// </summary>
    /// <param name="featureFlagService">フィーチャーフラグサービス</param>
    /// <returns>デバッグ機能が利用可能な場合true</returns>
    public static bool CanUseDebugFeatures(this IFeatureFlagService featureFlagService)
    {
        return featureFlagService.IsDebugFeaturesEnabled || featureFlagService.IsExperimentalFeaturesEnabled;
    }
}