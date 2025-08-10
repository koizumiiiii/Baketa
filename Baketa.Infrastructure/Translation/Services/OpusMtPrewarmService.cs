using System;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Translation.Models;
using Baketa.Infrastructure.Translation.Local;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation.Services;

/// <summary>
/// OPUS-MT翻訳エンジン事前ウォームアップサービス実装
/// 🔧 [TCP_STABILIZATION] 高優先タスク: 事前サーバー起動による60秒→0秒削減
/// </summary>
public class OpusMtPrewarmService : IOpusMtPrewarmService, IDisposable
{
    private readonly ILogger<OpusMtPrewarmService> _logger;
    private readonly TransformersOpusMtEngine _opusMtEngine;
    private volatile bool _isPrewarmed;
    private volatile string _prewarmStatus = "未開始";
    private bool _disposed;

    public OpusMtPrewarmService(
        ILogger<OpusMtPrewarmService> logger,
        TransformersOpusMtEngine opusMtEngine)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _opusMtEngine = opusMtEngine ?? throw new ArgumentNullException(nameof(opusMtEngine));
        
        Console.WriteLine("🔥 [PREWARM_DEBUG] OpusMtPrewarmService作成完了");
        _logger.LogInformation("OPUS-MT事前ウォームアップサービスが初期化されました");
    }

    /// <inheritdoc/>
    public bool IsPrewarmed => _isPrewarmed;

    /// <inheritdoc/>
    public string PrewarmStatus => _prewarmStatus;

    /// <inheritdoc/>
    public async Task StartPrewarmingAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            _logger.LogWarning("サービスが破棄されているため、プリウォーミングを開始できません");
            return;
        }

        _logger.LogInformation("🔥 [PREWARMING] OPUS-MT事前ウォームアップを開始します...");
        Console.WriteLine("🔥 [PREWARMING] OPUS-MT事前ウォームアップを開始します...");
        _prewarmStatus = "サーバー起動中...";

        // バックグラウンドでウォームアップを実行（メインアプリケーション起動をブロックしない）
        _ = Task.Run(async () =>
        {
            try
            {
                await PerformPrewarmingAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "事前ウォームアップ中にエラーが発生しました");
                _prewarmStatus = $"エラー: {ex.Message}";
            }
        }, cancellationToken);
    }

    private async Task PerformPrewarmingAsync(CancellationToken cancellationToken)
    {
        try
        {
            // フェーズ1: OPUS-MTエンジンの初期化
            Console.WriteLine("🔥 [PREWARMING] フェーズ1: OPUS-MTエンジン初期化開始");
            _prewarmStatus = "OPUS-MTエンジン初期化中...";
            
            await _opusMtEngine.InitializeAsync().ConfigureAwait(false);
            
            Console.WriteLine("✅ [PREWARMING] フェーズ1完了: OPUS-MTエンジン初期化完了");
            
            // フェーズ2: テスト翻訳実行（モデルロード確認）
            Console.WriteLine("🔥 [PREWARMING] フェーズ2: テスト翻訳開始");
            _prewarmStatus = "モデルウォームアップ中...";
            
            // 短いテスト文で英→日翻訳を実行
            var testText = "Hello";
            var testRequest = new TranslationRequest
            {
                SourceText = testText,
                SourceLanguage = Language.English,
                TargetLanguage = Language.Japanese
            };
            var testResult = await _opusMtEngine.TranslateAsync(testRequest, cancellationToken).ConfigureAwait(false);
            
            if (testResult.IsSuccess)
            {
                Console.WriteLine($"✅ [PREWARMING] フェーズ2完了: テスト翻訳成功 '{testText}' → '{testResult.TranslatedText}'");
                _prewarmStatus = "ウォームアップ完了";
                _isPrewarmed = true;
                
                _logger.LogInformation("🎉 [PREWARMING] OPUS-MT事前ウォームアップが正常に完了しました");
                Console.WriteLine("🎉 [PREWARMING] OPUS-MT事前ウォームアップが正常に完了しました");
            }
            else
            {
                throw new InvalidOperationException($"テスト翻訳が失敗しました: {testResult.Error?.Message ?? "不明なエラー"}");
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("事前ウォームアップがキャンセルされました");
            _prewarmStatus = "キャンセル済み";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "事前ウォームアップでエラーが発生しました: {Error}", ex.Message);
            _prewarmStatus = $"失敗: {ex.Message}";
            
            Console.WriteLine($"❌ [PREWARMING] ウォームアップエラー: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _logger.LogDebug("OpusMtPrewarmServiceがディスポーズされました");
        }
    }
}