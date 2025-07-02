using System;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Events;

namespace Baketa.Application.Services.Events;

/// <summary>
/// イベントプロセッサーの自動購読サービス
/// </summary>
public sealed class EventProcessorRegistrationService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IEventAggregator _eventAggregator;
    private readonly ILogger<EventProcessorRegistrationService> _logger;

    /// <summary>
    /// サービスを初期化します
    /// </summary>
    /// <param name="serviceProvider">サービスプロバイダー</param>
    /// <param name="eventAggregator">イベント集約器</param>
    /// <param name="logger">ロガー</param>
    public EventProcessorRegistrationService(
        IServiceProvider serviceProvider,
        IEventAggregator eventAggregator,
        ILogger<EventProcessorRegistrationService> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// サービス開始時にイベントプロセッサーを自動登録します
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>開始タスク</returns>
    public async System.Threading.Tasks.Task StartAsync(System.Threading.CancellationToken cancellationToken)
    {
        _logger.LogInformation("イベントプロセッサーの自動登録を開始します");

        try
        {
            await RegisterAllEventProcessorsAsync().ConfigureAwait(false);
            _logger.LogInformation("イベントプロセッサーの自動登録が完了しました");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "イベントプロセッサーの自動登録中にエラーが発生しました");
            throw;
        }
    }

    /// <summary>
    /// サービス停止時の処理
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>停止タスク</returns>
    public System.Threading.Tasks.Task StopAsync(System.Threading.CancellationToken cancellationToken)
    {
        _logger.LogInformation("EventProcessorRegistrationServiceを停止します");
        return System.Threading.Tasks.Task.CompletedTask;
    }

    /// <summary>
    /// すべてのイベントプロセッサーを検出し、イベント集約器に登録します
    /// </summary>
    private async System.Threading.Tasks.Task RegisterAllEventProcessorsAsync()
    {
        // アプリケーション層のアセンブリからイベントプロセッサータイプを検索
        var processorTypes = GetEventProcessorTypes();
        var registeredCount = 0;

        foreach (var processorType in processorTypes)
        {
            try
            {
                await RegisterEventProcessorAsync(processorType).ConfigureAwait(false);
                registeredCount++;
            }
            // イベントプロセッサ登録はシステム初期化の一部であり、个別プロセッサの失敗は全体を停止させない
            // CA1031: 個別のプロセッサ登録エラーはシステム初期化の継続のために適切に処理されます
#pragma warning disable CA1031
            catch (Exception ex)
            {
                _logger.LogError(ex, "プロセッサー {ProcessorType} の登録に失敗しました", processorType.Name);
            }
#pragma warning restore CA1031
        }

        _logger.LogInformation("{RegisteredCount} 個のイベントプロセッサーを登録しました", registeredCount);
    }

    /// <summary>
    /// アプリケーション層からイベントプロセッサータイプを取得します
    /// </summary>
    /// <returns>イベントプロセッサータイプのコレクション</returns>
    private static Type[] GetEventProcessorTypes()
    {
        var applicationAssembly = Assembly.GetAssembly(typeof(EventProcessorRegistrationService));
        if (applicationAssembly == null)
        {
            return [];
        }

        // IDE0305: C#12 コレクション式を使用して簡潔に表現
        return [
            .. applicationAssembly.GetTypes()
                .Where(type => 
                    type.IsClass && 
                    !type.IsAbstract && 
                    type.GetInterfaces()
                        .Any(i => i.IsGenericType && 
                                 i.GetGenericTypeDefinition() == typeof(IEventProcessor<>)))
        ];
    }

    /// <summary>
    /// 特定のイベントプロセッサータイプを登録します
    /// </summary>
    /// <param name="processorType">プロセッサータイプ</param>
    private async System.Threading.Tasks.Task RegisterEventProcessorAsync(Type processorType)
    {
        // サービスプロバイダーからプロセッサーインスタンスを取得
        var processor = _serviceProvider.GetService(processorType);
        if (processor == null)
        {
            _logger.LogWarning("プロセッサー {ProcessorType} がDIコンテナに登録されていません", processorType.Name);
            return;
        }

        // IEventProcessor<TEvent>インターフェースを取得
        var eventProcessorInterface = processorType.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && 
                               i.GetGenericTypeDefinition() == typeof(IEventProcessor<>));

        if (eventProcessorInterface == null)
        {
            _logger.LogWarning("プロセッサー {ProcessorType} は IEventProcessor<> インターフェースを実装していません", processorType.Name);
            return;
        }

        // イベント型を取得
        var eventType = eventProcessorInterface.GetGenericArguments()[0];

        // 動的にSubscribeメソッドを呼び出す
        var subscribeMethod = typeof(IEventAggregator)
            .GetMethod(nameof(IEventAggregator.Subscribe))
            ?.MakeGenericMethod(eventType);

        if (subscribeMethod == null)
        {
            _logger.LogError("Subscribe メソッドが見つかりません");
            return;
        }

        try
        {
            subscribeMethod.Invoke(_eventAggregator, [processor]);
            _logger.LogDebug("プロセッサー {ProcessorType} をイベント {EventType} に登録しました",
                processorType.Name, eventType.Name);
        }
        // メソッド呼び出しによるリフレクションエラーをログ記録し、システム初期化を継続します
        // CA1031: リフレクションエラーはシステム初期化の継続のために適切に処理されます
#pragma warning disable CA1031
        catch (Exception ex)
        {
            _logger.LogError(ex, "プロセッサー {ProcessorType} の動的登録に失敗しました", processorType.Name);
        }
#pragma warning restore CA1031

        await System.Threading.Tasks.Task.Delay(1).ConfigureAwait(false); // 非同期処理のサンプル
    }
}
