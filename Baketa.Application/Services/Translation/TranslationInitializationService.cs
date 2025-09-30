using System;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Events.EventTypes;
using Baketa.Infrastructure.Translation.Local;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Baketa.Application.Services.Translation;

/// <summary>
/// 翻訳サービス初期化HostedService
/// 🎯 UltraPhase 13.1: STEP4無限待機問題の根本解決
/// 🚀 DI循環依存回避しつつPython翻訳サーバーの確実な初期化を実現
/// 📋 Gemini AI推奨: BackgroundServiceパターンによるClean Architecture準拠実装
/// 🆕 UltraThink Phase 2: StartButton制御のためのPythonServerStatusChangedEvent発行追加
/// </summary>
public class TranslationInitializationService : BackgroundService
{
    private readonly ITranslationEngine _translationEngine;
    private readonly IEventAggregator _eventAggregator;
    private readonly ILogger<TranslationInitializationService> _logger;

    public TranslationInitializationService(
        ITranslationEngine translationEngine,
        IEventAggregator eventAggregator,
        ILogger<TranslationInitializationService> logger)
    {
        Console.WriteLine("🚀 [CTOR_START] TranslationInitializationService コンストラクター開始");
        Console.WriteLine($"🔍 [CTOR_PARAM] translationEngine: {translationEngine?.GetType().Name ?? "NULL"}");
        Console.WriteLine($"🔍 [CTOR_PARAM] eventAggregator: {eventAggregator?.GetType().Name ?? "NULL"}");
        Console.WriteLine($"🔍 [CTOR_PARAM] logger: {logger?.GetType().Name ?? "NULL"}");

        _translationEngine = translationEngine ?? throw new ArgumentNullException(nameof(translationEngine));
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        Console.WriteLine("✅ [CTOR_END] TranslationInitializationService コンストラクター完了");
        _logger.LogInformation("🚀 TranslationInitializationService コンストラクター完了 - エンジン型: {EngineType}",
            translationEngine.GetType().Name);
        Console.WriteLine($"🚀 TranslationInitializationService コンストラクター完了 - エンジン型: {translationEngine.GetType().Name}");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Console.WriteLine("🚀 TranslationInitializationService ExecuteAsync 開始");
        try
        {
            Console.WriteLine("🔍 [UltraPhase 14.11] ステップ1: tryブロック進入");
            _logger.LogInformation("🚀 [INIT_SERVICE] 翻訳サービス初期化開始");
            Console.WriteLine("🚀 [INIT_SERVICE] 翻訳サービス初期化開始");

            Console.WriteLine("🔍 [UltraPhase 14.11] ステップ2: 翻訳エンジン型チェック開始");
            Console.WriteLine($"🔍 [UltraPhase 14.11] _translationEngine型: {_translationEngine?.GetType()?.Name ?? "NULL"}");

            // OptimizedPythonTranslationEngineの場合のみ初期化実行
            if (_translationEngine is OptimizedPythonTranslationEngine optimizedEngine)
            {
                Console.WriteLine("🔍 [UltraPhase 14.11] ステップ3: OptimizedPythonTranslationEngine型確認成功");
                _logger.LogInformation("✅ [INIT_SERVICE] OptimizedPythonTranslationEngine検出 - 初期化実行開始");
                Console.WriteLine("✅ [INIT_SERVICE] OptimizedPythonTranslationEngine検出 - 初期化実行開始");

                Console.WriteLine("🔍 [UltraPhase 14.11] ステップ4: InitializeAsync呼び出し直前");

                // 🔧 UltraPhase 14.8.2: Task.Run()でHostedServiceデッドロック回避
                var result = await Task.Run(async () =>
                {
                    Console.WriteLine("🔍 [UltraPhase 14.11] ステップ5: Task.Run内でInitializeAsync実行開始");
                    var initResult = await optimizedEngine.InitializeAsync().ConfigureAwait(false);
                    Console.WriteLine($"🔍 [UltraPhase 14.11] ステップ6: InitializeAsync結果: {initResult}");
                    return initResult;
                });

                Console.WriteLine($"🔍 [UltraPhase 14.11] ステップ7: Task.Run完了 - 結果: {result}");
                _logger.LogInformation("🎉 [INIT_SERVICE] OptimizedPythonTranslationEngine初期化完了 - Python服务器起動成功");
                Console.WriteLine("🎉 [INIT_SERVICE] OptimizedPythonTranslationEngine初期化完了 - Python服务器起動成功");

                // 🆕 UltraThink Phase 2: StartButton制御のためのイベント発行
                Console.WriteLine("📡 [INIT_SERVICE] PythonServerStatusChangedEvent発行開始 - UIのStartButton有効化");
                await PublishServerReadyEventAsync().ConfigureAwait(false);
                Console.WriteLine("✅ [INIT_SERVICE] PythonServerStatusChangedEvent発行完了");
            }
            else
            {
                Console.WriteLine("🔍 [UltraPhase 14.11] ステップ3: OptimizedPythonTranslationEngine以外を検出");
                _logger.LogInformation("ℹ️ [INIT_SERVICE] 初期化不要な翻訳エンジン: {EngineType}",
                    _translationEngine.GetType().Name);
                Console.WriteLine($"ℹ️ [INIT_SERVICE] 初期化不要な翻訳エンジン: {_translationEngine.GetType().Name}");

                // 🆕 UltraThink Phase 2: 非Optimizedエンジンでも即座に準備完了通知
                await PublishServerReadyEventAsync().ConfigureAwait(false);
            }

            Console.WriteLine("🔍 [UltraPhase 14.11] ステップ8: 正常終了処理");
            _logger.LogInformation("✅ [INIT_SERVICE] 翻訳サービス初期化プロセス完了");
            Console.WriteLine("✅ [INIT_SERVICE] 翻訳サービス初期化プロセス完了");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"🚨 [UltraPhase 14.11] 例外キャッチ: {ex.GetType().Name} - {ex.Message}");
            Console.WriteLine($"🚨 [UltraPhase 14.11] スタックトレース: {ex.StackTrace}");
            _logger.LogError(ex, "❌ [INIT_SERVICE] 翻訳サービス初期化失敗 - アプリケーション起動を中止");

            // 初期化失敗はアプリケーション起動失敗として扱う
            // これによりHostedServiceの起動失敗がアプリケーション全体に伝播する
            throw;
        }
    }

    /// <summary>
    /// 🆕 UltraThink Phase 2: 翻訳サーバー準備完了イベント発行
    /// UIのStartButton制御のため、初期化完了時にPythonServerStatusChangedEventを発行
    /// </summary>
    private async Task PublishServerReadyEventAsync()
    {
        try
        {
            var statusEvent = PythonServerStatusChangedEvent.CreateServerReady(
                port: 0, // ポート番号は動的に割り当てられているため0で代替
                details: "TranslationInitializationService: 翻訳エンジン初期化完了");

            await _eventAggregator.PublishAsync(statusEvent).ConfigureAwait(false);

            _logger.LogInformation("📡 [INIT_SERVICE] PythonServerStatusChangedEvent発行成功 - UIのStartButton有効化完了");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ [INIT_SERVICE] PythonServerStatusChangedEvent発行エラー - UIへの通知失敗");
        }
    }
}