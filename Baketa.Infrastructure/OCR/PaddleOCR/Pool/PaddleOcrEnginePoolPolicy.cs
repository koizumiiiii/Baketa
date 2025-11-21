using Baketa.Core.Abstractions.OCR;
using Baketa.Infrastructure.OCR.PaddleOCR.Factory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;

namespace Baketa.Infrastructure.OCR.PaddleOCR.Pool;

/// <summary>
/// PaddleOCRエンジンプール管理ポリシー
/// ObjectPoolでのエンジンインスタンスライフサイクル管理
/// </summary>
public sealed class PaddleOcrEnginePoolPolicy(
    IPaddleOcrEngineFactory engineFactory,
    ILogger<PaddleOcrEnginePoolPolicy> logger) : IPooledObjectPolicy<IOcrEngine>
{
    private readonly IPaddleOcrEngineFactory _engineFactory = engineFactory ?? throw new ArgumentNullException(nameof(engineFactory));
    private readonly ILogger<PaddleOcrEnginePoolPolicy> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// プール用の新しいエンジンインスタンスを作成
    /// </summary>
    public IOcrEngine Create()
    {
        try
        {
            _logger.LogDebug("🏊 PaddleOcrEnginePoolPolicy: プール用エンジンインスタンス作成開始");

            // ファクトリーを使用してエンジンを非同期作成
            // Note: IPooledObjectPolicyは同期メソッドのため、結果を同期取得
            var engine = _engineFactory.CreateAsync().GetAwaiter().GetResult();

            _logger.LogDebug("✅ PaddleOcrEnginePoolPolicy: エンジンインスタンス作成完了 - Hash: {EngineHash}, 型: {EngineType}",
                engine.GetHashCode(), engine.GetType().Name);

            return engine;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ PaddleOcrEnginePoolPolicy: エンジンインスタンス作成でエラー");
            throw;
        }
    }

    /// <summary>
    /// エンジンインスタンスがプールに返却される際の処理
    /// </summary>
    public bool Return(IOcrEngine obj)
    {
        if (obj == null)
        {
            _logger.LogWarning("⚠️ PaddleOcrEnginePoolPolicy: null エンジンの返却を拒否");
            return false;
        }

        try
        {
            _logger.LogDebug("🔄 PaddleOcrEnginePoolPolicy: エンジン返却処理開始 - Hash: {EngineHash}",
                obj.GetHashCode());

            // エンジンの再利用可能性を確認
            if (!_engineFactory.IsReusable(obj))
            {
                _logger.LogWarning("⚠️ PaddleOcrEnginePoolPolicy: エンジンが再利用不可 - 破棄 Hash: {EngineHash}",
                    obj.GetHashCode());

                // エンジンを破棄
                DisposeEngine(obj);
                return false;
            }

            // クリーンアップを実行（非同期メソッドを同期実行）
            _engineFactory.CleanupAsync(obj).GetAwaiter().GetResult();

            _logger.LogDebug("✅ PaddleOcrEnginePoolPolicy: エンジン返却処理完了 - プールに復帰 Hash: {EngineHash}",
                obj.GetHashCode());

            return true; // プールに返却
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ PaddleOcrEnginePoolPolicy: エンジン返却処理でエラー - 破棄 Hash: {EngineHash}",
                obj.GetHashCode());

            // エラー時はエンジンを破棄
            DisposeEngine(obj);
            return false;
        }
    }

    /// <summary>
    /// エンジンインスタンスを安全に破棄
    /// </summary>
    private void DisposeEngine(IOcrEngine engine)
    {
        try
        {
            _logger.LogDebug("🗑️ PaddleOcrEnginePoolPolicy: エンジン破棄開始 - Hash: {EngineHash}",
                engine.GetHashCode());

            if (engine is IDisposable disposableEngine)
            {
                disposableEngine.Dispose();
            }

            _logger.LogDebug("✅ PaddleOcrEnginePoolPolicy: エンジン破棄完了");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ PaddleOcrEnginePoolPolicy: エンジン破棄時にエラー - 続行");
        }
    }
}
