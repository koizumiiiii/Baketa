using Baketa.Core.Abstractions.Events;
using Microsoft.Extensions.DependencyInjection;

namespace Baketa.Core.Events.Implementation;

/// <summary>
/// イベント集約機構のサービス登録拡張メソッド
/// </summary>
public static class EventAggregatorServiceExtensions
{
    /// <summary>
    /// イベント集約機構をサービスコレクションに登録します
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <returns>設定済みのサービスコレクション</returns>
    public static IServiceCollection AddEventAggregator(this IServiceCollection services)
    {
        // イベント集約機構をシングルトンとして登録（完全修飾名使用）
        services.AddSingleton<Baketa.Core.Abstractions.Events.IEventAggregator, EventAggregator>();

        return services;
    }
}
