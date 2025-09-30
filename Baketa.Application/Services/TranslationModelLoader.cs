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
    private volatile bool _isInitializing = false;
    private readonly object _initializationLock = new();

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
        // 🔍 UltraPhase 9.2: 重複初期化防止ロック
        lock (_initializationLock)
        {
            if (_isInitialized)
            {
                var alreadyInitializedMessage = "✅ [INIT_SKIP] 翻訳モデルは既に初期化済み - スキップ";
                Console.WriteLine(alreadyInitializedMessage);
                Baketa.Core.Logging.BaketaLogManager.LogSystemDebug(alreadyInitializedMessage);
                return;
            }

            if (_isInitializing)
            {
                var alreadyInitializingMessage = "⏳ [INIT_WAIT] 別スレッドで初期化実行中 - 重複呼び出しをスキップ";
                Console.WriteLine(alreadyInitializingMessage);
                Baketa.Core.Logging.BaketaLogManager.LogSystemDebug(alreadyInitializingMessage);
                return;
            }

            _isInitializing = true;
        }

        // 🔍 UltraPhase 9.1: メソッド開始の即座ログ
        Console.WriteLine("🔍 [INIT_ASYNC_START] TranslationModelLoader.InitializeAsync() メソッド開始");
        Baketa.Core.Logging.BaketaLogManager.LogSystemDebug("🔍 [INIT_ASYNC_START] TranslationModelLoader.InitializeAsync() メソッド開始");

        try
        {
            // 🔧 UltraPhase 9.3: _logger呼び出しでハング発生のため一時的に無効化
            Console.WriteLine("🔥 [PRELOAD_START] 翻訳モデル事前ロード開始");
            Baketa.Core.Logging.BaketaLogManager.LogSystemDebug("🔥 [PRELOAD_START] 翻訳モデル事前ロード開始");
            OnProgressChanged("開始", 0);

            Console.WriteLine("🔄 [PRELOAD_INIT] OptimizedPythonTranslationEngine初期化中...");
            Baketa.Core.Logging.BaketaLogManager.LogSystemDebug("🔄 [PRELOAD_INIT] OptimizedPythonTranslationEngine初期化中...");
            OnProgressChanged("初期化中", 25);

            // OptimizedPythonTranslationEngineの初期化
            if (_translationEngine is Baketa.Infrastructure.Translation.Local.OptimizedPythonTranslationEngine engine)
            {
                Console.WriteLine("🔧 [PRELOAD_TYPECAST] OptimizedPythonTranslationEngine型キャスト成功");
                Baketa.Core.Logging.BaketaLogManager.LogSystemDebug("🔧 [PRELOAD_TYPECAST] OptimizedPythonTranslationEngine型キャスト成功");

                // 🔍 UltraPhase 9.5: engine.InitializeAsync()呼び出し前後のトレース
                Console.WriteLine("🔍 [ENGINE_INIT_BEFORE] engine.InitializeAsync()呼び出し直前");
                Baketa.Core.Logging.BaketaLogManager.LogSystemDebug("🔍 [ENGINE_INIT_BEFORE] engine.InitializeAsync()呼び出し直前");

                await engine.InitializeAsync().ConfigureAwait(false);

                Console.WriteLine("🔍 [ENGINE_INIT_AFTER] engine.InitializeAsync()呼び出し完了");
                Baketa.Core.Logging.BaketaLogManager.LogSystemDebug("🔍 [ENGINE_INIT_AFTER] engine.InitializeAsync()呼び出し完了");
            }
            else
            {
                var actualType = _translationEngine?.GetType().FullName ?? "null";
                Console.WriteLine($"❌ [PRELOAD_TYPECAST_FAILED] _translationEngine型キャスト失敗 - 実際の型: {actualType}");
                Baketa.Core.Logging.BaketaLogManager.LogSystemDebug($"❌ [PRELOAD_TYPECAST_FAILED] _translationEngine型キャスト失敗 - 実際の型: {actualType}");

                // 🔧 Phase 2.2.3: 動的キャスト試行
                var optimizedEngine = _translationEngine as Baketa.Infrastructure.Translation.Local.OptimizedPythonTranslationEngine;
                if (optimizedEngine != null)
                {
                    Console.WriteLine("🔧 [PRELOAD_DYNAMIC_CAST] 動的キャスト成功 - InitializeAsync実行");
                    Baketa.Core.Logging.BaketaLogManager.LogSystemDebug("🔧 [PRELOAD_DYNAMIC_CAST] 動的キャスト成功 - InitializeAsync実行");
                    await optimizedEngine.InitializeAsync().ConfigureAwait(false);
                }
                else
                {
                    Console.WriteLine("❌ [PRELOAD_DYNAMIC_CAST_FAILED] 動的キャストも失敗 - DIコンテナ登録問題");
                    Baketa.Core.Logging.BaketaLogManager.LogSystemDebug("❌ [PRELOAD_DYNAMIC_CAST_FAILED] 動的キャストも失敗 - DIコンテナ登録問題");
                }
            }

            Console.WriteLine("🧠 [PRELOAD_MODEL] NLLB-200モデルロード中 (2.4GB)...");
            Baketa.Core.Logging.BaketaLogManager.LogSystemDebug("🧠 [PRELOAD_MODEL] NLLB-200モデルロード中 (2.4GB)...");
            OnProgressChanged("モデルロード中", 75);

            // モデルロード完了確認
            if (await _translationEngine.IsReadyAsync().ConfigureAwait(false))
            {
                lock (_initializationLock)
                {
                    _isInitialized = true;
                    _isInitializing = false;
                }
                Console.WriteLine("✅ [PRELOAD_SUCCESS] 翻訳エンジン準備完了 - 初回翻訳は即座実行可能");
                Baketa.Core.Logging.BaketaLogManager.LogSystemDebug("✅ [PRELOAD_SUCCESS] 翻訳エンジン準備完了 - 初回翻訳は即座実行可能");
                OnProgressChanged("完了", 100, true);
            }
            else
            {
                lock (_initializationLock)
                {
                    _isInitializing = false;
                }
                Console.WriteLine("⚠️ [PRELOAD_PARTIAL] 翻訳エンジン初期化完了したが準備未完了");
                Baketa.Core.Logging.BaketaLogManager.LogSystemDebug("⚠️ [PRELOAD_PARTIAL] 翻訳エンジン初期化完了したが準備未完了");
                OnProgressChanged("部分完了", 90, false);
            }
        }
        catch (Exception ex)
        {
            lock (_initializationLock)
            {
                _isInitializing = false;
            }
            Console.WriteLine($"⚠️ [PRELOAD_FAILED] 事前ロード失敗 - 従来の遅延初期化に戻ります: {ex.Message}");
            Baketa.Core.Logging.BaketaLogManager.LogSystemDebug($"⚠️ [PRELOAD_FAILED] 事前ロード失敗: {ex.Message}");
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