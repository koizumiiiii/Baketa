using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.OCR;
using Baketa.Infrastructure.OCR.PaddleOCR.Abstractions;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models;

namespace Baketa.Infrastructure.OCR.PaddleOCR.Services;

/// <summary>
/// PaddleOcrAllエンジンの初期化、設定適用、ウォームアップを担当するサービス
/// Phase 2.6: PaddleOcrEngineから抽出されたエンジン初期化実装
/// </summary>
public sealed class PaddleOcrEngineInitializer : IPaddleOcrEngineInitializer, IDisposable
{
    private readonly IPaddleOcrUtilities _utilities;
    private readonly ILogger<PaddleOcrEngineInitializer>? _logger;

    private PaddleOcrAll? _ocrEngine;
    private QueuedPaddleOcrAll? _queuedEngine;
    private readonly object _lockObject = new();

    public PaddleOcrEngineInitializer(
        IPaddleOcrUtilities utilities,
        ILogger<PaddleOcrEngineInitializer>? logger = null)
    {
        _utilities = utilities ?? throw new ArgumentNullException(nameof(utilities));
        _logger = logger;
        _logger?.LogInformation("🚀 PaddleOcrEngineInitializer初期化完了");
    }

    /// <summary>
    /// ネイティブライブラリチェック
    /// </summary>
    public bool CheckNativeLibraries()
    {
        try
        {
            // テスト環境での安全性チェックを強化
            if (_utilities.IsTestEnvironment())
            {
                _logger?.LogDebug("テスト環境でのネイティブライブラリチェックをスキップ");
                return false; // テスト環境では安全のため初期化を失敗させる
            }

            // OpenCV初期化テスト - バージョン 4.10.0.20240616 対応
            using var testMat = new Mat(1, 1, MatType.CV_8UC3);

            // 基本的なプロパティアクセスでライブラリの動作を確認
            var width = testMat.Width;
            var height = testMat.Height;

            _logger?.LogDebug("ネイティブライブラリのチェック成功 - OpenCvSharp4 v4.10+ (Size: {Width}x{Height})", width, height);
            return true;
        }
        catch (TypeInitializationException ex)
        {
            _logger?.LogError(ex, "ネイティブライブラリ初期化エラー: {ExceptionType}", ex.GetType().Name);
            return false;
        }
        catch (DllNotFoundException ex)
        {
            _logger?.LogError(ex, "ネイティブライブラリが見つかりません: {ExceptionType}", ex.GetType().Name);
            return false;
        }
        catch (FileNotFoundException ex)
        {
            _logger?.LogError(ex, "必要なファイルが見つかりません: {ExceptionType}", ex.GetType().Name);
            return false;
        }
        catch (BadImageFormatException ex)
        {
            _logger?.LogError(ex, "ネイティブライブラリ形式エラー: {ExceptionType}", ex.GetType().Name);
            return false;
        }
        catch (InvalidOperationException ex)
        {
            _logger?.LogError(ex, "ネイティブライブラリ操作エラー: {ExceptionType}", ex.GetType().Name);
            return false;
        }
    }

    /// <summary>
    /// エンジン初期化
    /// </summary>
    public async Task<bool> InitializeEnginesAsync(
        FullOcrModel models,
        OcrEngineSettings settings,
        CancellationToken cancellationToken)
    {
        // Gemini推奨：スレッドセーフティ問題解決のため、一時的にCPUモード、シングルスレッドに強制
        if (true) // デバッグ用：常に適用
        {
            settings.UseGpu = false;
            settings.EnableMultiThread = false;
            settings.WorkerCount = 1;
            _logger?.LogDebug("🔧 スレッドセーフティ検証のため、CPU/シングルスレッドモードに強制設定");
        }

        try
        {
            // PaddleOcrAllの安全な初期化（診断トレーシング簡素化）
            var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            combinedCts.CancelAfter(TimeSpan.FromMinutes(2)); // 2分でタイムアウト

            var taskCompletionSource = new TaskCompletionSource<bool>();

            // UI スレッドでの初期化を避けるためにTask.Runを使用
            _ = Task.Run(async () =>
            {
                try
                {
                    // PaddleOcrAllの作成（正しいFullOcrModelを使用）
                    lock (_lockObject)
                    {
                        _ocrEngine = new PaddleOcrAll(models)
                        {
                            AllowRotateDetection = true,
                            Enable180Classification = false // 🛡️ [CRASH_FIX] AccessViolationException回避
                            // 根本原因: PaddleOcrClassifier.ShouldRotate180()内でPD_PredictorRunがメモリアクセス違反
                            // 180度回転テキストは未対応となるが、ゲーム翻訳では実用上問題なし
                        };
                    }

                    // 🔥 [PHASE13.2.2_FIX] OCR認識精度向上 - 最適化パラメーター有効化
                    // UltraThink Phase 1で特定: このコメントアウトがOCR文字化けの根本原因
                    // 効果: det_db_thresh 0.3→0.1, det_db_box_thresh 0.6→0.3, 解像度960→1440

                    // 🔥 [PHASE13.2.5_DIAGNOSTIC] Console.WriteLine診断ログ追加（Logger null対策）
                    Console.WriteLine($"🚨🚨🚨 [PHASE13.2.5] InitializeAsync実行中 - _logger is null: {_logger == null}");
                    Console.WriteLine($"🚨🚨🚨 [PHASE13.2.5] ApplyDetectionOptimization呼び出し直前");

                    try
                    {
                        // 検出感度向上パラメーター適用（低コントラスト・小文字対応）
                        ApplyDetectionOptimization(_ocrEngine);
                        Console.WriteLine("✅✅✅ [PHASE13.2.5] ApplyDetectionOptimization呼び出し成功");
                        _logger?.LogInformation("✅ [PHASE13.2.2] PaddleOCR検出精度最適化パラメーター適用完了");
                    }
                    catch (Exception optEx)
                    {
                        Console.WriteLine($"❌❌❌ [PHASE13.2.5] ApplyDetectionOptimization失敗: {optEx.Message}");
                        _logger?.LogWarning(optEx, "⚠️ PaddleOCR最適化パラメーター適用で警告発生（処理継続）");
                    }

                    _logger?.LogDebug("✅ PaddleOcrAll作成完了 - エンジン型: {EngineType}", _ocrEngine?.GetType()?.Name);

                    // Gemini推奨：初期化パラメータの確認
                    _logger?.LogDebug("🔧 OCRエンジン初期化パラメータ:");
                    _logger?.LogDebug("   UseGpu: {UseGpu}", settings.UseGpu);
                    _logger?.LogDebug("   EnableMultiThread: {EnableMultiThread}", settings.EnableMultiThread);
                    _logger?.LogDebug("   WorkerCount: {WorkerCount}", settings.WorkerCount);
                    _logger?.LogDebug("   Language: {Language}", settings.Language);

                    await Task.Delay(50, combinedCts.Token).ConfigureAwait(false); // わずかな初期化遅延
                    taskCompletionSource.SetResult(true);
                }
                catch (OperationCanceledException) when (combinedCts.Token.IsCancellationRequested)
                {
                    _logger?.LogWarning("PaddleOCRエンジン初期化がタイムアウトしました");
                    taskCompletionSource.SetResult(false);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "PaddleOCRエンジン初期化エラー: {ExceptionType}", ex.GetType().Name);
                    taskCompletionSource.SetException(ex);
                }
            }, combinedCts.Token);

            return await taskCompletionSource.Task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "OCRエンジンの安全な初期化に失敗しました");
            return false;
        }
    }

    /// <summary>
    /// ウォームアップ
    /// </summary>
    public async Task<bool> WarmupAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger?.LogInformation("🔥 PaddleOCRウォームアップ開始");
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // OCRエンジンが初期化されているか確認
            if (_ocrEngine == null)
            {
                _logger?.LogWarning("OCRエンジンが初期化されていないため、ウォームアップをスキップ");
                return false;
            }

            _logger?.LogInformation("📝 ダミー画像でOCR実行中...");

            // 🔧 [GEMINI_FIX] Matを直接作成することで、AdvancedImage→バイト配列→Matの二重処理を回避
            // 512x512の白い画像 (CV_8UC3: 3チャンネル、8ビット符号なし)
            try
            {
                using var mat = new Mat(512, 512, MatType.CV_8UC3, Scalar.White);

                if (!mat.Empty())
                {
                    // Task.Runでワーカースレッドにオフロードし、UIスレッドをブロックしない
                    await Task.Run(() =>
                    {
                        var result = _ocrEngine.Run(mat);
                        _logger?.LogDebug("🔍 ウォームアップOCR結果: 検出領域数={Count}", result.Regions.Length);
                    }, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "⚠️ ウォームアップOCR実行で警告発生（処理継続）");
            }

            stopwatch.Stop();
            _logger?.LogInformation("✅ PaddleOCRウォームアップ完了: {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);

            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "❌ PaddleOCRウォームアップ中にエラーが発生");
            return false;
        }
    }

    /// <summary>
    /// OCRエンジン取得
    /// </summary>
    public PaddleOcrAll? GetOcrEngine()
    {
        lock (_lockObject)
        {
            return _ocrEngine;
        }
    }

    /// <summary>
    /// キューイング型OCRエンジン取得
    /// </summary>
    public QueuedPaddleOcrAll? GetQueuedEngine()
    {
        lock (_lockObject)
        {
            return _queuedEngine;
        }
    }

    /// <summary>
    /// エンジン再初期化（内部実装）
    /// </summary>
    public async Task ReinitializeEngineAsync(OcrEngineSettings settings, FullOcrModel models, CancellationToken cancellationToken)
    {
        try
        {
            _logger?.LogInformation("🔄 OCRエンジン再初期化開始");

            // 現在のエンジンを安全に廃棄
            lock (_lockObject)
            {
                _queuedEngine?.Dispose();
                _queuedEngine = null;

                // 🔧 [GEMINI_FIX] PaddleOcrAllもDisposeを呼び出す
                (_ocrEngine as IDisposable)?.Dispose();
                _ocrEngine = null;
            }

            // 短い待機時間でメモリクリーンアップ
            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            GC.Collect();
            GC.WaitForPendingFinalizers();

            // エンジンを再初期化
            var success = await InitializeEnginesAsync(models, settings, cancellationToken).ConfigureAwait(false);

            if (success)
            {
                _logger?.LogInformation("✅ OCRエンジン再初期化成功");
            }
            else
            {
                _logger?.LogWarning("⚠️ OCRエンジン再初期化失敗");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "❌ OCRエンジン再初期化中にエラーが発生");
        }
    }

    #region 内部実装メソッド

    /// <summary>
    /// 検出精度最適化パラメーター適用（内部実装）
    /// </summary>
    private void ApplyDetectionOptimization(PaddleOcrAll ocrEngine)
    {
        // 🔥 [PHASE13.2.5_DIAGNOSTIC] メソッド開始ログ
        Console.WriteLine("🔥🔥🔥 [PHASE13.2.5] ApplyDetectionOptimization メソッド開始");

        try
        {
            var engineType = ocrEngine.GetType();
            Console.WriteLine($"🔥 [PHASE13.2.5] EngineType取得成功: {engineType?.Name}");

            // 🎯 検出感度最適化パラメーター（言語非依存）
            var detectionParams = new Dictionary<string, object>
            {
                // 検出閾値を大幅に下げて感度向上（0.3 → 0.1）
                { "det_db_thresh", 0.1f },

                // ボックス閾値を下げて小さなテキストも検出（0.6 → 0.3）
                { "det_db_box_thresh", 0.3f },

                // アンクリップ比率を上げて小さい文字を拡張
                { "det_db_unclip_ratio", 2.2f },

                // 🔥 [PHASE13.2.12_FIX] Gemini推奨: det_limit_side_len を 1440 → 960 にロールバック
                // 根本原因: 4K画像(3840x2160)を1440に縮小する際、OpenCV内部で "_step >= minstep" エラー発生
                // 修正内容: PaddleOCR公式デフォルト値960に戻すことで、安定した動作を確保
                { "det_limit_side_len", 960 },

                // スコアモードを精度重視に設定
                { "det_db_score_mode", "slow" },

                // 検出制限タイプ
                { "det_limit_type", "max" }
            };

            Console.WriteLine($"🔥 [PHASE13.2.5] 最適化パラメータ数: {detectionParams.Count}");

            // リフレクションでパラメーター適用
            int appliedCount = 0;
            foreach (var param in detectionParams)
            {
                try
                {
                    // プロパティ検索
                    var property = engineType.GetProperty(param.Key,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                    if (property != null && property.CanWrite)
                    {
                        var convertedValue = ConvertParameterValue(param.Value, property.PropertyType);
                        property.SetValue(ocrEngine, convertedValue);
                        appliedCount++;
                        continue;
                    }

                    // フィールド検索
                    var field = engineType.GetField(param.Key,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                    if (field != null)
                    {
                        var convertedValue = ConvertParameterValue(param.Value, field.FieldType);
                        field.SetValue(ocrEngine, convertedValue);
                        appliedCount++;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "パラメーター適用エラー {ParamKey}", param.Key);
                }
            }

            // 🔥 [PHASE13.2.5_DIAGNOSTIC] パラメータ適用結果ログ
            Console.WriteLine($"✅✅✅ [PHASE13.2.5] 検出精度最適化完了: {appliedCount}/{detectionParams.Count}個のパラメーター適用");
            _logger?.LogDebug("🎯 検出精度最適化完了: {AppliedCount}/{TotalCount}個のパラメーター適用",
                appliedCount, detectionParams.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "検出最適化エラー");
            throw;
        }
    }

    /// <summary>
    /// パラメーター値の型変換
    /// </summary>
    private static object? ConvertParameterValue(object value, Type targetType)
    {
        if (value == null) return null;

        if (targetType == typeof(string))
            return value.ToString();

        if (targetType == typeof(bool))
            return Convert.ToBoolean(value, System.Globalization.CultureInfo.InvariantCulture);

        if (targetType == typeof(int))
            return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);

        if (targetType == typeof(float))
            return Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture);

        if (targetType == typeof(double))
            return Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);

        return Convert.ChangeType(value, targetType, System.Globalization.CultureInfo.InvariantCulture);
    }

    #endregion

    #region IDisposable実装

    private bool _disposed;

    /// <summary>
    /// リソースを解放します
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        lock (_lockObject)
        {
            _queuedEngine?.Dispose();
            _queuedEngine = null;

            // 🔧 [GEMINI_FIX] PaddleOcrAllはIDisposableを実装しているため、Disposeを呼び出す
            // 内部の推論器(PaddleOcrDetector, PaddleOcrClassifier, PaddleOcrRecognizer)が解放される
            (_ocrEngine as IDisposable)?.Dispose();
            _ocrEngine = null;
        }

        _disposed = true;
        _logger?.LogDebug("PaddleOcrEngineInitializer破棄完了");
    }

    #endregion
}
