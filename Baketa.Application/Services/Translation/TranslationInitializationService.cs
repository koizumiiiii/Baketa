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

            Console.WriteLine($"🔍 [PHASE3.1] 翻訳エンジン型: {_translationEngine?.GetType()?.Name ?? "NULL"}");

            // 🔥 [PHASE3.1] すべてのITranslationEngineでInitializeAsync呼び出し（型チェック不要）
            _logger.LogInformation("✅ [PHASE3.1] 翻訳エンジン初期化開始: {EngineType}", _translationEngine.GetType().Name);
            Console.WriteLine($"✅ [PHASE3.1] 翻訳エンジン初期化開始: {_translationEngine.GetType().Name}");

            // Task.Run()でHostedServiceデッドロック回避
            var result = await Task.Run(async () =>
            {
                Console.WriteLine("🔍 [PHASE3.1] Task.Run内でInitializeAsync実行開始");
                var initResult = await _translationEngine.InitializeAsync().ConfigureAwait(false);
                Console.WriteLine($"🔍 [PHASE3.1] InitializeAsync結果: {initResult}");
                return initResult;
            });

            Console.WriteLine($"🔍 [PHASE3.1] Task.Run完了 - 結果: {result}");
            _logger.LogInformation("🎉 [PHASE3.1] 翻訳エンジン初期化完了: {EngineType}", _translationEngine.GetType().Name);
            Console.WriteLine($"🎉 [PHASE3.1] 翻訳エンジン初期化完了: {_translationEngine.GetType().Name}");

            // StartButton制御のためのイベント発行
            Console.WriteLine("📡 [PHASE3.1] PythonServerStatusChangedEvent発行開始 - UIのStartButton有効化");
            await PublishServerReadyEventAsync().ConfigureAwait(false);
            Console.WriteLine("✅ [PHASE3.1] PythonServerStatusChangedEvent発行完了");

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