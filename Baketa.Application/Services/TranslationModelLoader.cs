using System;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Translation;
using Microsoft.Extensions.Logging;

namespace Baketa.Application.Services;

/// <summary>
/// 翻訳モデル事前ロードサービス実装
/// Phase 1: 翻訳モデル事前ロード戦略 - Clean Architecture準拠実装
/// 目的: 初回翻訳時の6秒待機問題をバックグラウンドロードで解決
/// </summary>
public class TranslationModelLoader : IApplicationInitializer
{
    private readonly ITranslationEngine _translationEngine;
    private readonly ILogger<TranslationModelLoader> _logger;
    private volatile bool _isInitialized = false;

    /// <summary>
    /// 初期化完了状態
    /// </summary>
    public bool IsInitialized => _isInitialized;

    /// <summary>
    /// 初期化進捗変更イベント
    /// </summary>
    public event EventHandler<InitializationProgressEventArgs>? ProgressChanged;

    /// <summary>
    /// TranslationModelLoaderコンストラクタ
    /// </summary>
    /// <param name="translationEngine">翻訳エンジンインターフェース</param>
    /// <param name="logger">ロガーインスタンス</param>
    public TranslationModelLoader(
        ITranslationEngine translationEngine,
        ILogger<TranslationModelLoader> logger)
    {
        _translationEngine = translationEngine ?? throw new ArgumentNullException(nameof(translationEngine));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 翻訳モデル事前ロード非同期実行
    /// UIスレッドをブロックせずにバックグラウンドでNLLB-200モデル（2.4GB）をロード
    /// </summary>
    /// <returns>初期化完了タスク</returns>
    public async Task InitializeAsync()
    {
        try
        {
            _logger.LogInformation("🔥 [PRELOAD_START] 翻訳モデル事前ロード開始");
            OnProgressChanged("開始", 0);

            _logger.LogInformation("🔄 [PRELOAD_INIT] OptimizedPythonTranslationEngine初期化中...");
            OnProgressChanged("初期化中", 25);

            // OptimizedPythonTranslationEngineの初期化
            if (_translationEngine is Baketa.Infrastructure.Translation.Local.OptimizedPythonTranslationEngine engine)
            {
                await engine.InitializeAsync().ConfigureAwait(false);
            }

            _logger.LogInformation("🧠 [PRELOAD_MODEL] NLLB-200モデルロード中 (2.4GB)...");
            OnProgressChanged("モデルロード中", 75);

            // モデルロード完了確認
            if (await _translationEngine.IsReadyAsync().ConfigureAwait(false))
            {
                _isInitialized = true;
                _logger.LogInformation("✅ [PRELOAD_SUCCESS] 翻訳エンジン準備完了 - 初回翻訳は即座実行可能");
                OnProgressChanged("完了", 100, true);
            }
            else
            {
                _logger.LogWarning("⚠️ [PRELOAD_PARTIAL] 翻訳エンジン初期化完了したが準備未完了");
                OnProgressChanged("部分完了", 90, false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ [PRELOAD_FAILED] 事前ロード失敗 - 従来の遅延初期化に戻ります: {Message}", ex.Message);
            OnProgressChanged("失敗", 0, false, ex);

            // 🎯 重要: 失敗してもアプリケーション継続（フォールバック動作）
            // 従来の遅延初期化で翻訳機能は利用可能
        }
    }

    /// <summary>
    /// 進捗変更イベント発火
    /// </summary>
    /// <param name="stage">現在の段階</param>
    /// <param name="progress">進捗率（0-100）</param>
    /// <param name="isCompleted">完了フラグ</param>
    /// <param name="error">エラー情報</param>
    private void OnProgressChanged(string stage, int progress, bool isCompleted = false, Exception? error = null)
    {
        ProgressChanged?.Invoke(this, new InitializationProgressEventArgs
        {
            Stage = stage,
            ProgressPercentage = progress,
            IsCompleted = isCompleted,
            Error = error
        });
    }
}