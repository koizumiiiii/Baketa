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
        // 🔥 [P4-A_FIX] デバッグコード削除完了 - QueuedPaddleOcrAllによりスレッドセーフティ保証済み
        // GPU/マルチスレッド設定は settings.UseGpu, settings.EnableMultiThread に従う

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
                    // 🔥 [P1-B-FIX_PHASE1] QueuedPaddleOcrAll作成（スレッドセーフ保証）
                    // Gemini推奨: 各ワーカーが独立したPaddleOcrAllインスタンスを持つ
                    // 🚀 [P1-B-FIX_PHASE3] consumerCount=4: Phase2検証完了後の並列度最適化（2→4）
                    lock (_lockObject)
                    {
                        _queuedEngine = new QueuedPaddleOcrAll(
                            factory: () =>
                            {
                                var engine = new PaddleOcrAll(models)
                                {
                                    AllowRotateDetection = true, // ✅ [PHASE10.26_REVERT] commit 09e1fc3の正常動作設定に戻す - false設定が原因で検出激減（8→1個）
                                    Enable180Classification = false // 🛡️ [CRASH_FIX] AccessViolationException回避
                                };

                                // 🔥 [PHASE10.26_DEBUG_A] 設定確認ログ
                                Console.WriteLine($"🔥 [DEBUG_A] PaddleOcrAll作成直後: AllowRotateDetection={engine.AllowRotateDetection}");
                                _logger?.LogDebug("🔥 [DEBUG_A] PaddleOcrAll作成直後: AllowRotateDetection={AllowRotateDetection}", engine.AllowRotateDetection);

                                // 🔥 [PHASE13.2.2_FIX] 各ワーカーインスタンスに検出最適化適用
                                try
                                {
                                    ApplyDetectionOptimization(engine);
                                    _logger?.LogDebug("✅ [P1-B-FIX] ワーカーインスタンスに検出最適化適用完了");
                                }
                                catch (Exception optEx)
                                {
                                    _logger?.LogWarning(optEx, "⚠️ ワーカーインスタンス最適化で警告（処理継続）");
                                }

                                // 🔥 [PHASE10.26_DEBUG_A] 最終確認ログ
                                Console.WriteLine($"🔥 [DEBUG_A] factory return直前: AllowRotateDetection={engine.AllowRotateDetection}");
                                _logger?.LogDebug("🔥 [DEBUG_A] factory return直前: AllowRotateDetection={AllowRotateDetection}", engine.AllowRotateDetection);

                                return engine;
                            },
                            consumerCount: settings.QueuedOcrConsumerCount,  // 🔥 [P4-B_FIX] 設定外部化（appsettings.json対応）
                            boundedCapacity: settings.QueuedOcrBoundedCapacity // 🔥 [P4-B_FIX] 設定外部化（appsettings.json対応）
                        );

                        _logger?.LogInformation("✅ [P4-B_FIX] QueuedPaddleOcrAll初期化完了 - consumerCount: {ConsumerCount}, boundedCapacity: {BoundedCapacity}",
                            settings.QueuedOcrConsumerCount, settings.QueuedOcrBoundedCapacity);
                        Console.WriteLine($"✅ [P4-B_FIX] QueuedPaddleOcrAll初期化完了 - consumerCount: {settings.QueuedOcrConsumerCount}, boundedCapacity: {settings.QueuedOcrBoundedCapacity}");
                    }

                    _logger?.LogDebug("✅ [P4-B_FIX] QueuedPaddleOcrAll作成完了 - ワーカー数: {ConsumerCount}（設定値）", settings.QueuedOcrConsumerCount);

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

            // 🔥 [P1-B-FIX] QueuedPaddleOcrAll初期化確認
            if (_queuedEngine == null)
            {
                _logger?.LogWarning("QueuedPaddleOcrAll初期化されていないため、ウォームアップをスキップ");
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
                    // 🔥 [P1-B-FIX] QueuedPaddleOcrAllはTask<PaddleOcrResult>を返すためawait必須
                    await Task.Run(async () =>
                    {
                        var result = await _queuedEngine.Run(mat).ConfigureAwait(false);
                        _logger?.LogDebug("🔍 [P1-B-FIX] QueuedOCRウォームアップ結果: 検出領域数={Count}", result.Regions.Length);
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
        // ✅ [PPOCRV5_2025] パラメータ適用開始ログ
        _logger?.LogInformation("🔥 [PPOCRV5_2025] ApplyDetectionOptimization メソッド開始");

        try
        {
            var engineType = ocrEngine.GetType();
            _logger?.LogInformation("🔥 [PPOCRV5_2025] EngineType取得成功: {EngineType}", engineType?.Name);

            // 🔍 [DEBUG] PaddleOcrAllの利用可能なプロパティを列挙
            var availableProperties = engineType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Select(p => $"{p.Name} ({p.PropertyType.Name})")
                .ToList();
            _logger?.LogInformation("🔍 [PPOCRV5_2025] PaddleOcrAll利用可能なプロパティ ({Count}個): {Properties}",
                availableProperties.Count, string.Join(", ", availableProperties.Take(20)));

            // 🔥 [PPOCRV5_2025_FIX] Detectorオブジェクトを取得してパラメータ適用
            var detectorProperty = engineType.GetProperty("Detector");
            if (detectorProperty == null)
            {
                _logger?.LogWarning("⚠️ [PPOCRV5_2025] Detectorプロパティが見つかりません");
                return;
            }

            var detector = detectorProperty.GetValue(ocrEngine);
            if (detector == null)
            {
                _logger?.LogWarning("⚠️ [PPOCRV5_2025] Detectorオブジェクトがnullです");
                return;
            }

            var detectorType = detector.GetType();
            _logger?.LogInformation("🔥 [PPOCRV5_2025] Detectorオブジェクト取得成功: {DetectorType}", detectorType.Name);

            // ✅ [PPOCRV5_2025] PP-OCRv5公式推奨パラメータ適用（2025年ベストプラクティス）
            // 参考: https://paddlepaddle.github.io/PaddleOCR/main/en/version3.x/algorithm/PP-OCRv5/PP-OCRv5.html
            // 🔥 実際のDetectorプロパティ名にマッピング: BoxScoreThreahold, BoxThreshold, UnclipRatio, MaxSize
            var detectionParams = new Dictionary<string, object>
            {
                // 検出閾値: PP-OCRv5推奨値 0.3（ノイズ削減、精度向上）
                // 旧値 0.1 は過度に緩く偽陽性増加の原因
                // Note: プロパティ名にtypoあり（Threshold → Threahold）
                { "BoxScoreThreahold", 0.3f },

                // ボックス閾値: PP-OCRv5推奨値 0.6（偽陽性削減 -40%）
                // 旧値 0.3 は低信頼度ボックスを過剰検出
                { "BoxThreshold", 0.6f },

                // アンクリップ比率: PP-OCRv5推奨値 1.5（座標精度 +15%）
                // 旧値 2.2 は過度な拡張で座標ズレの原因
                { "UnclipRatio", 1.5f },

                // 最大サイズ: 960に維持
                // 理由: 4K画像(3840x2160)を1440に縮小時、OpenCV内部でエラー発生
                // PP-OCRv5推奨64への変更は副作用リスク高く、Phase 2で慎重検証予定
                { "MaxSize", 960 }
            };

            _logger?.LogInformation("🔥 [PPOCRV5_2025] 最適化パラメータ数: {ParamCount}", detectionParams.Count);

            // リフレクションでパラメーター適用（Detectorオブジェクトに対して）
            int appliedCount = 0;
            foreach (var param in detectionParams)
            {
                try
                {
                    // Detectorのプロパティ検索
                    var property = detectorType.GetProperty(param.Key,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                    if (property != null && property.CanWrite)
                    {
                        var convertedValue = ConvertParameterValue(param.Value, property.PropertyType);
                        property.SetValue(detector, convertedValue);
                        appliedCount++;
                        _logger?.LogInformation("✅ [PPOCRV5_2025] パラメータ適用成功: {ParamKey} = {ParamValue}", param.Key, param.Value);
                    }
                    else
                    {
                        _logger?.LogWarning("⚠️ [PPOCRV5_2025] パラメータ未適用: {ParamKey} (Property not found or read-only)", param.Key);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "❌ [PPOCRV5_2025] パラメータ適用エラー: {ParamKey}", param.Key);
                }
            }

            // ✅ [PPOCRV5_2025] パラメータ適用結果ログ
            _logger?.LogInformation("✅ [PPOCRV5_2025] 検出精度最適化完了: {AppliedCount}/{TotalCount}個のパラメーター適用",
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

        // 🔥 [PPOCRV5_2025_FIX] Nullable<T>型の処理
        var underlyingType = Nullable.GetUnderlyingType(targetType);
        if (underlyingType != null)
        {
            // Nullable<T>の場合、T型に変換してからNullable<T>を作成
            var convertedValue = ConvertParameterValue(value, underlyingType);
            return convertedValue;
        }

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
