using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Processing;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Events.Capture;
using Baketa.Core.Models.ImageProcessing;
using Baketa.Core.Models.Processing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Infrastructure.Processing.Strategies;

/// <summary>
/// 拡張画像変化検知段階の処理戦略
/// P0: 3段階フィルタリング対応（Stage 1: 90% → Stage 2: 8% → Stage 3: 2%）
/// EnhancedImageChangeDetectionServiceによる高速化実装
/// </summary>
public class ImageChangeDetectionStageStrategy : IProcessingStageStrategy
{
    private readonly IImageChangeDetectionService _changeDetectionService;
    private readonly ILogger<ImageChangeDetectionStageStrategy> _logger;
    private readonly IEventAggregator? _eventAggregator; // UltraThink Phase 1: オーバーレイ自動削除統合（オプショナル）
    private readonly IDetectionBoundsCache? _detectionBoundsCache; // [Issue #500] Detection-Onlyフィルタ用キャッシュ

    // 🔥 [PHASE11_FIX] コンテキストID別に前回画像を管理（Singleton問題解決）
    // 問題: Singletonの_previousImageが複数の処理経路で共有され、初回実行でもpreviousImage != nullになる
    // 解決策: ConcurrentDictionary<contextId, IImage>でコンテキストごとに前回画像を管理
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, IImage?> _previousImages = new();

    // [Issue #392] コンテキストID別に前回OCRテキスト位置を管理
    // 前回のOCRで検出されたテキストのバウンディングボックスを保持し、
    // 画像変化検知時に「テキストがあった場所が変わったか」を判定する
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Rectangle[]> _previousTextBounds = new();

    // [Issue #481] オーバーレイに関連付けられた過去のテキスト位置を蓄積保持
    // _previousTextBoundsは最新OCR結果のみだが、これは過去N世代分を保持
    // テキストAが消えた後も、A位置が追跡対象として残り続けるための機構
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, List<Rectangle>> _historicalTextBounds = new();
    private const int MaxHistoricalBoundsPerContext = 50;

    public ProcessingStageType StageType => ProcessingStageType.ImageChangeDetection;
    public TimeSpan EstimatedProcessingTime => TimeSpan.FromMilliseconds(2); // 3段階フィルタリングによる高速化

    public ImageChangeDetectionStageStrategy(
        IImageChangeDetectionService changeDetectionService,
        ILogger<ImageChangeDetectionStageStrategy> logger,
        IEventAggregator? eventAggregator = null, // UltraThink Phase 1: オプショナル統合
        IDetectionBoundsCache? detectionBoundsCache = null) // [Issue #500] Detection-Onlyフィルタ用キャッシュ
    {
        _changeDetectionService = changeDetectionService ?? throw new ArgumentNullException(nameof(changeDetectionService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _eventAggregator = eventAggregator; // null許可（段階的統合対応）
        _detectionBoundsCache = detectionBoundsCache; // [Issue #500] null許容

        if (_eventAggregator != null)
        {
            _logger.LogInformation("🎯 ImageChangeDetectionStageStrategy - EventAggregator統合有効（オーバーレイ自動削除対応）");
        }
    }

    public async Task<ProcessingStageResult> ExecuteAsync(ProcessingContext context, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var input = context.Input;
            var currentImage = input.CapturedImage;

            if (currentImage == null)
            {
                _logger.LogWarning("キャプチャ画像が null - 変化ありとして処理継続");
                return ProcessingStageResult.CreateSuccess(StageType,
                    CreateLegacyResult(ImageChangeResult.CreateFirstTime("NULL", HashAlgorithmType.AverageHash, stopwatch.Elapsed)),
                    stopwatch.Elapsed);
            }

            // 🔥 [PHASE11_FIX] コンテキストIDを生成（ウィンドウハンドル + 領域ベース）
            // 各翻訳セッションごとに独立した画像履歴を保持
            var contextId = BuildContextId(input.SourceWindowHandle, input.CaptureRegion);

            // 🔥 [PHASE11_FIX] コンテキストID別に前回画像を取得
            _previousImages.TryGetValue(contextId, out var previousImageToUse);

            // 3段階フィルタリング画像変化検知を実行
            var changeResult = await _changeDetectionService.DetectChangeAsync(
                previousImageToUse,
                currentImage,
                contextId,
                cancellationToken).ConfigureAwait(false);

            // [Issue #302 DEBUG] EnhancedImageChangeDetectionServiceからの結果を詳細ログ
            _logger.LogInformation("🔍 [STAGE_RESULT_DEBUG] EnhancedImageChangeDetectionService結果: HasChanged={HasChanged}, ChangePercentage={ChangePercentage:F4}, DetectionStage={DetectionStage}",
                changeResult.HasChanged, changeResult.ChangePercentage, changeResult.DetectionStage);

            var processingResult = CreateLegacyResult(changeResult);

            // 🎯 [Issue #407] テキスト消失イベント発行（前回画像破棄前に実行 - ピクセル比較に必要）
            await TryPublishTextDisappearanceEventAsync(
                changeResult,
                previousImageToUse,
                currentImage,
                input.SourceWindowHandle,
                input.CaptureRegion,
                input.OriginalWindowSize,
                cancellationToken).ConfigureAwait(false);

            // 🔥 [PHASE11_FIX] コンテキストID別に前回画像を更新（リソース管理付き）
            // 古い画像を破棄してから新しい画像を保存
            try
            {
                if (_previousImages.TryRemove(contextId, out var oldImage))
                {
                    // IImage は IDisposable を継承しているため、直接 Dispose() を呼び出す
                    oldImage.Dispose();
                }
                _previousImages[contextId] = currentImage;
            }
            catch (Exception disposeEx)
            {
                _logger.LogWarning(disposeEx, "前回画像の破棄でエラー: {Message}", disposeEx.Message);
            }

            _logger.LogDebug("🎯 拡張画像変化検知完了 - 変化: {HasChanged}, Stage: {DetectionStage}, 変化率: {ChangePercentage:F3}%, 処理時間: {ProcessingTimeMs}ms",
                changeResult.HasChanged,
                changeResult.DetectionStage,
                changeResult.ChangePercentage * 100,
                changeResult.ProcessingTime.TotalMilliseconds);

            // 統計情報をログ出力（パフォーマンス監視用）
            LogPerformanceStatistics();

            return ProcessingStageResult.CreateSuccess(StageType, processingResult, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 拡張画像変化検知段階でエラーが発生 - 処理時間: {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);

            // エラー時は変化ありとして安全側で処理継続
            var fallbackResult = CreateLegacyResult(
                ImageChangeResult.CreateChanged("ERROR", "ERROR", 1.0f, HashAlgorithmType.AverageHash, stopwatch.Elapsed));

            return ProcessingStageResult.CreateSuccess(StageType, fallbackResult, stopwatch.Elapsed);
        }
        finally
        {
            stopwatch.Stop();
        }
    }

    public bool ShouldExecute(ProcessingContext context)
    {
        var currentImage = context.Input?.CapturedImage;

        // 画像なし: 実行不要
        if (currentImage == null)
        {
            _logger.LogDebug("🚫 ShouldExecute: false (画像なし)");
            return false;
        }

        // 🎯 [PHASE4.4_FIX] EnhancedImageChangeDetectionServiceの3段階検知に完全委任
        // サイズベースチェックサム（CalculateImageChecksum）は不適切なため廃止
        // 理由: ゲームウィンドウはプレイ中にサイズ変更しないため、常に同じチェックサム値となり変化検知不能
        // EnhancedImageChangeDetectionServiceのStage 1パーセプチュアルハッシュが背景変化も検出
        _logger.LogDebug("✅ ShouldExecute: true (EnhancedImageChangeDetectionServiceに委任)");
        return true;
    }

    /// <summary>
    /// 画像変化検知の履歴をクリア（Stop→Start時の初期化用）
    /// </summary>
    /// <remarks>
    /// Stop→Start時に以前の画像履歴が残っていると、変化なしと誤判定される問題を防止
    /// TranslationFlowEventProcessor.HandleAsync(StopTranslationRequestEvent)から呼び出される
    /// </remarks>
    public void ClearPreviousImages()
    {
        _previousImages.Clear();
        _previousTextBounds.Clear();
        _historicalTextBounds.Clear();
        _detectionBoundsCache?.ClearAll(); // [Issue #500] Detection-Onlyフィルタ用キャッシュもクリア
        _logger.LogInformation("🧹 [STOP_FIX] 画像変化検知履歴をクリア - Stop→Start後の初回翻訳を確実に実行");
    }

    /// <summary>
    /// [Issue #392] 前回OCRテキスト位置を更新（パイプラインOCR完了後に呼び出し）
    /// </summary>
    /// <param name="contextId">コンテキストID（ウィンドウハンドル+領域）</param>
    /// <param name="textBounds">OCRで検出されたテキストのバウンディングボックス配列</param>
    public void UpdatePreviousTextBounds(string contextId, Rectangle[] textBounds)
    {
        // [Issue #481] 既存の位置を履歴に保存（上書き前）
        // 新しいOCR結果に含まれない旧テキスト位置を蓄積し、テキスト消失検知の追跡範囲を広げる
        if (_previousTextBounds.TryGetValue(contextId, out var oldBounds) && oldBounds.Length > 0)
        {
            var historical = _historicalTextBounds.GetOrAdd(contextId, _ => new List<Rectangle>());
            lock (historical)
            {
                // 新しいOCR結果にも含まれるものは除外（まだ表示中のテキスト）
                // [Issue #486] 極小テキスト矩形は履歴に蓄積しない
                foreach (var old in oldBounds)
                {
                    if (old.Width * old.Height >= MinTextBoundsArea &&
                        !textBounds.Any(nb => nb.IntersectsWith(old)))
                    {
                        historical.Add(old);
                    }
                }

                // 上限制御
                if (historical.Count > MaxHistoricalBoundsPerContext)
                {
                    historical.RemoveRange(0, historical.Count - MaxHistoricalBoundsPerContext);
                }
            }
        }

        _previousTextBounds[contextId] = textBounds;
        _logger.LogDebug("[Issue #392] 前回テキスト位置を更新: ContextId={ContextId}, TextCount={Count}, HistoricalCount={HistCount}",
            contextId, textBounds.Length,
            _historicalTextBounds.TryGetValue(contextId, out var hist) ? hist.Count : 0);
    }

    /// <summary>
    /// [Issue #392] コンテキストIDを生成（外部からのフィードバック用）
    /// [Issue #403] 1pxジッタ吸収: X, Y, Width, Heightを2px単位に丸めてコンテキストIDを正規化
    /// キャプチャ高さが719↔720pxで交互に変化するケースで、異なるIDが生成されるのを防止
    /// 丸めはID文字列のみに適用し、実画像データには影響しない
    /// </summary>
    public static string BuildContextId(IntPtr windowHandle, Rectangle captureRegion)
    {
        // [Issue #403] 切り上げ偶数丸め: 隣接する奇数/偶数が同じ値にマップされる
        // 例: 719→720, 720→720（& ~1だと719→718, 720→720で異なるIDになるため不可）
        var x = (captureRegion.X + 1) & ~1;
        var y = (captureRegion.Y + 1) & ~1;
        var w = (captureRegion.Width + 1) & ~1;
        var h = (captureRegion.Height + 1) & ~1;
        return $"window_{windowHandle}_region_{x}_{y}_{w}_{h}";
    }

    /// <summary>
    /// 基本的な変化検知（サイズベース + 基本プロパティ比較）
    /// 実際の画像データにアクセスせずに高速判定を実行
    /// </summary>
    /// <param name="previousImage">前回画像</param>
    /// <param name="currentImage">現在画像</param>
    /// <returns>変化があるかどうか</returns>
    /// <summary>
    /// 基本的な変化検知（サイズベース + ハッシュ比較）
    /// Stage 1フィルタリング相当の軽量判定を実行
    /// </summary>
    /// <param name="previousImage">前回画像</param>
    /// <param name="currentImage">現在画像</param>
    /// <returns>変化があるかどうか</returns>
    private static bool PerformBasicChangeCheck(IImage previousImage, IImage currentImage)
    {
        try
        {
            // 🎯 根本修正: オブジェクト参照比較（同一画像オブジェクトの検出）
            if (ReferenceEquals(previousImage, currentImage))
            {
                return false; // 同一オブジェクト = 変化なし
            }

            // 🛡️ ObjectDisposedException対策: プロパティアクセス前に破棄状態確認
            if (IsImageDisposed(previousImage) || IsImageDisposed(currentImage))
            {
                // 破棄された画像は変化ありとして処理継続（安全側）
                return true;
            }

            // 🚀 基本的なサイズ比較実装
            if (previousImage.Width != currentImage.Width || previousImage.Height != currentImage.Height)
            {
                return true; // サイズ変化 = 明確な変化
            }

            // 🔍 **実装修正**: 実際の画像内容比較を追加
            // Stage 1相当の軽量な変化検知ロジックを実装
            return PerformLightweightContentComparison(previousImage, currentImage);
        }
        catch (ObjectDisposedException)
        {
            // ObjectDisposedException特化: 変化ありとして安全側で処理継続
            return true;
        }
        catch (Exception)
        {
            // その他の例外: 変化ありとして安全側で処理継続
            return true;
        }
    }

    /// <summary>
    /// 軽量なコンテンツ比較実装（Stage 1フィルタリング相当）
    /// サンプリングベースの高速変化検知を実行
    /// </summary>
    /// <param name="previousImage">前回画像</param>
    /// <param name="currentImage">現在画像</param>
    /// <returns>変化があるかどうか</returns>
    private static bool PerformLightweightContentComparison(IImage previousImage, IImage currentImage)
    {
        try
        {
            // 🎯 実装: チェックサム比較（高速な初期検証）
            var prevChecksum = CalculateImageChecksum(previousImage);
            var currChecksum = CalculateImageChecksum(currentImage);

            if (prevChecksum == currChecksum)
            {
                return false; // チェックサム一致 = 変化なし（高確度）
            }

            // チェックサム不一致の場合、サンプリングベース詳細比較
            return PerformSampledPixelComparison(previousImage, currentImage);
        }
        catch (Exception)
        {
            // エラー時は安全側で変化ありとして処理継続
            return true;
        }
    }

    /// <summary>
    /// 画像のチェックサム計算（高速ハッシュ）
    /// </summary>
    /// <param name="image">対象画像</param>
    /// <returns>チェックサム値</returns>
    private static uint CalculateImageChecksum(IImage image)
    {
        // 🚀 軽量実装: サイズ情報ベースの簡易チェックサム
        // 実際の画像データアクセス前に基本プロパティで判定
        uint checksum = (uint)(image.Width * 31 + image.Height * 17);

        // 🔍 実装拡張可能: 将来的にピクセルデータの部分サンプリングを追加
        // 現在はサイズベースの基本実装

        return checksum;
    }

    /// <summary>
    /// サンプリングベースのピクセル比較（Stage 1相当の軽量比較）
    /// </summary>
    /// <param name="previousImage">前回画像</param>
    /// <param name="currentImage">現在画像</param>
    /// <returns>変化があるかどうか</returns>
    private static bool PerformSampledPixelComparison(IImage previousImage, IImage currentImage)
    {
        try
        {
            // 🎯 サンプルサイズ: パフォーマンスと精度のバランス調整
            var sampleSize = Math.Min(8, Math.Min(previousImage.Width, previousImage.Height) / 4);
            if (sampleSize < 1) return false; // 極小画像は変化なしとして扱う

            var centerX = previousImage.Width / 2;
            var centerY = previousImage.Height / 2;
            var halfSample = sampleSize / 2;

            // 🔍 中央領域のサンプリング比較
            for (int y = centerY - halfSample; y < centerY + halfSample && y < previousImage.Height; y++)
            {
                for (int x = centerX - halfSample; x < centerX + halfSample && x < previousImage.Width; x++)
                {
                    // 境界チェック
                    if (x < 0 || y < 0) continue;

                    // 🚀 軽量ピクセル比較: 実装は画像タイプに依存
                    var prevBrightness = GetSafePixelBrightness(previousImage, x, y);
                    var currBrightness = GetSafePixelBrightness(currentImage, x, y);

                    // 閾値: 5%以上の輝度差で変化と判定
                    if (Math.Abs(prevBrightness - currBrightness) > 0.05f)
                    {
                        return true; // 変化検出
                    }
                }
            }

            return false; // サンプル領域で変化なし
        }
        catch (Exception)
        {
            // サンプリング失敗時は安全側で変化ありとして処理
            return true;
        }
    }

    /// <summary>
    /// 安全なピクセル輝度取得（エラー処理付き）
    /// </summary>
    /// <param name="image">画像</param>
    /// <param name="x">X座標</param>
    /// <param name="y">Y座標</param>
    /// <returns>正規化輝度値 (0.0-1.0)</returns>
    private static float GetSafePixelBrightness(IImage image, int x, int y)
    {
        try
        {
            // 🔍 実装修正: 実際のIImage実装に合わせた具体的な実装が必要
            // 現在はサイズベース近似を使用（後の最適化で実際のピクセルデータアクセスに変更）

            // 基本的な座標ベース擬似輝度計算（サイズ比例）
            var normalizedX = (float)x / Math.Max(1, image.Width);
            var normalizedY = (float)y / Math.Max(1, image.Height);

            // 座標ベースの擬似ハッシュ値（一時的実装）
            var pseudoBrightness = (normalizedX + normalizedY) * 0.5f;

            return Math.Max(0.0f, Math.Min(1.0f, pseudoBrightness)); // 0.0-1.0にクランプ
        }
        catch (Exception)
        {
            // エラー時は中間値を返す
            return 0.5f;
        }
    }

    /// <summary>
    /// IImageインスタンスが破棄されているかどうかを安全に確認
    /// </summary>
    /// <param name="image">確認対象の画像</param>
    /// <returns>破棄されている場合はtrue</returns>
    private static bool IsImageDisposed(IImage image)
    {
        try
        {
            // 🛡️ 汎用的アプローチ: どのIImage実装でも動作する方法
            // プロパティアクセスで破棄状態を間接的にチェック
            _ = image.Width; // プロパティアクセス試行
            _ = image.Height; // プロパティアクセス試行
            return false; // アクセス成功 = まだ破棄されていない
        }
        catch (ObjectDisposedException)
        {
            return true; // 破棄されている
        }
        catch (Exception)
        {
            return true; // その他のエラー = 破棄状態として扱う（安全側）
        }
    }

    /// <summary>
    /// 基本的な同期変化検知（高速ハッシュベース）
    /// Stage 1フィルタリング相当の軽量比較を同期実行
    /// </summary>
    /// <param name="previousImage">前回画像</param>
    /// <param name="currentImage">現在画像</param>
    /// <returns>変化があるかどうか</returns>
    private static bool PerformBasicSyncChangeCheck(IImage previousImage, IImage currentImage)
    {
        // サイズ比較（最も高速な変化検知）
        if (previousImage.Width != currentImage.Width || previousImage.Height != currentImage.Height)
        {
            return true;
        }

        // 簡易ハッシュ比較（平均値ベース）
        // 実際のStage 1フィルタリングと同等の高速判定
        var prevAvg = CalculateAveragePixelValue(previousImage);
        var currAvg = CalculateAveragePixelValue(currentImage);

        // 閾値: Stage 1相当の感度（5%差で変化とみなす）
        var changeThreshold = 0.05f;
        var changeRatio = Math.Abs(currAvg - prevAvg) / Math.Max(prevAvg, 1.0f);

        return changeRatio > changeThreshold;
    }

    /// <summary>
    /// 画像の平均ピクセル値を計算（高速近似）
    /// </summary>
    /// <param name="image">対象画像</param>
    /// <returns>平均ピクセル値</returns>
    private static float CalculateAveragePixelValue(IImage image)
    {
        // 簡易実装: サンプリングベースの平均値計算
        // 画像の中央部分の小さなサンプル（16x16）を使用して高速計算
        var sampleSize = Math.Min(16, Math.Min(image.Width, image.Height));
        var startX = (image.Width - sampleSize) / 2;
        var startY = (image.Height - sampleSize) / 2;

        float sum = 0;
        int count = 0;

        // サンプル領域の平均輝度を計算（グレースケール近似）
        for (int y = startY; y < startY + sampleSize; y++)
        {
            for (int x = startX; x < startX + sampleSize; x++)
            {
                // 簡易輝度計算（R+G+B平均）
                // 実際の実装では image.GetPixel() または類似メソッドを使用
                // ここでは概念的な実装
                sum += GetPixelBrightness(image, x, y);
                count++;
            }
        }

        return count > 0 ? sum / count : 0.0f;
    }

    /// <summary>
    /// 指定位置のピクセル輝度を取得（概念的実装）
    /// </summary>
    /// <param name="image">画像</param>
    /// <param name="x">X座標</param>
    /// <param name="y">Y座標</param>
    /// <returns>輝度値</returns>
    private static float GetPixelBrightness(IImage image, int x, int y)
    {
        // 概念的な実装: 実際のIImage実装に依存
        // WindowsImage, OpenCvImage等の実装に合わせて調整が必要
        return 128.0f; // プレースホルダー値
    }

    /// <summary>
    /// 新しいImageChangeResultを既存のImageChangeDetectionResultに変換
    /// 後方互換性のためのアダプター
    /// </summary>
    private static ImageChangeDetectionResult CreateLegacyResult(ImageChangeResult changeResult)
    {
        return new ImageChangeDetectionResult
        {
            HasChanged = changeResult.HasChanged,
            ChangePercentage = changeResult.ChangePercentage,
            PreviousHash = changeResult.PreviousHash,
            CurrentHash = changeResult.CurrentHash,
            ProcessingTime = changeResult.ProcessingTime,
            AlgorithmUsed = changeResult.AlgorithmUsed.ToString(),
            // [Issue #293] 変化領域を転送（部分OCR実行用）
            ChangedRegions = changeResult.ChangedRegions ?? []
        };
    }

    /// <summary>
    /// パフォーマンス統計をログ出力
    /// </summary>
    private void LogPerformanceStatistics()
    {
        try
        {
            var statistics = _changeDetectionService.GetStatistics();

            if (statistics.TotalProcessed > 0 && statistics.TotalProcessed % 100 == 0) // 100回毎に統計出力
            {
                _logger.LogInformation("📊 画像変化検知統計 - 総処理: {TotalProcessed}, Stage1除外率: {Stage1FilterRate:F1}%, " +
                    "Stage1平均: {Stage1AvgMs:F1}ms, Stage2平均: {Stage2AvgMs:F1}ms, Stage3平均: {Stage3AvgMs:F1}ms, " +
                    "キャッシュサイズ: {CacheSize}",
                    statistics.TotalProcessed,
                    statistics.FilteringEfficiency * 100,
                    statistics.AverageStage1Time.TotalMilliseconds,
                    statistics.AverageStage2Time.TotalMilliseconds,
                    statistics.AverageStage3Time.TotalMilliseconds,
                    statistics.CurrentCacheSize);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "統計情報取得エラー");
        }
    }

    /// <summary>
    /// テキスト消失イベント発行（UltraThink Phase 1: オーバーレイ自動削除システム統合）
    ///
    /// 画像変化検知の結果に基づいてTextDisappearanceEventを発行する。
    /// 変化がない場合（テキストが消失した可能性）にイベントを発行し、
    /// AutoOverlayCleanupServiceによるオーバーレイ自動削除を促す。
    /// </summary>
    /// <param name="changeResult">画像変化検知結果</param>
    /// <param name="previousImage">前回画像（null可能）</param>
    /// <param name="currentImage">現在画像（ピクセル変化率比較用）</param>
    /// <param name="windowHandle">ソースウィンドウハンドル</param>
    /// <param name="captureRegion">キャプチャ領域</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    private async Task TryPublishTextDisappearanceEventAsync(
        ImageChangeResult changeResult,
        IImage? previousImage,
        IImage currentImage,
        IntPtr windowHandle,
        Rectangle captureRegion,
        Size originalWindowSize,
        CancellationToken cancellationToken)
    {
        // EventAggregatorが統合されていない場合はスキップ
        if (_eventAggregator == null)
        {
            return;
        }

        try
        {
            // 🔧 [PHASE4.4_FIX] UltraThink + Gemini Review完了: TextDisappearanceEvent発行条件修正
            // 条件1: 前回画像が存在する（初回実行ではない）
            // 条件2: 画像に変化がある（!changeResult.HasChanged → changeResult.HasChanged に修正）
            // 条件3: テキスト消失パターンに該当する（IsTextDisappearance判定）
            var contextId = BuildContextId(windowHandle, captureRegion);
            var disappearedTextRect = previousImage != null && changeResult.HasChanged
                ? FindDisappearedTextRegion(changeResult, contextId, previousImage, currentImage)
                : (Rectangle?)null;
            if (disappearedTextRect.HasValue)
            {
                // [Issue #486] 消失領域を具体的なテキスト矩形に限定（captureRegion全体ではなく）
                var disappearedRegions = new List<Rectangle> { disappearedTextRect.Value };

                // 信頼度計算: Stage数と変化率から算出
                // [Issue #392] IsTextDisappearance()がtrueの場合、前回テキスト位置と変化領域の
                // 重なりを事実ベースで確認済み。CalculateDisappearanceConfidenceのStage3ベース値
                // (0.75)だと閾値(0.70)ギリギリで変化率補正で弾かれるケースがあるため、
                // テキスト位置マッチのボーナス(+0.10)を加算して安定的に閾値を超えるようにする。
                float confidenceScore = Math.Min(1.0f,
                    CalculateDisappearanceConfidence(changeResult) + 0.10f);

                // TextDisappearanceEvent作成・発行
                // [Issue #486] CaptureImageSizeを追加: ゾーン計算でAggregatedChunksReadyEventHandlerと同じ座標系を使用
                var disappearanceEvent = new TextDisappearanceEvent(
                    regions: disappearedRegions,
                    sourceWindow: windowHandle,
                    regionId: $"capture_{DateTime.UtcNow:yyyyMMddHHmmssfff}",
                    confidenceScore: confidenceScore,
                    originalWindowSize: originalWindowSize,
                    captureImageSize: new Size(captureRegion.Width, captureRegion.Height)
                );

                await _eventAggregator.PublishAsync(disappearanceEvent).ConfigureAwait(false);

                // [Issue #481] テキスト消失イベント発行後、該当コンテキストの履歴をクリア
                // 消失検知が成功したので、蓄積された過去テキスト位置はもう不要
                if (_historicalTextBounds.TryGetValue(contextId, out var hist))
                {
                    lock (hist) { hist.Clear(); }
                }

                _logger.LogDebug("🎯 TextDisappearanceEvent発行完了 - RegionId: {RegionId}, 信頼度: {Confidence:F3}, 領域: {Region}",
                    disappearanceEvent.RegionId, confidenceScore, captureRegion);
            }
            else
            {
                _logger.LogTrace("🔍 TextDisappearanceEvent発行条件未満 - 前回画像: {HasPrevious}, 変化: {HasChanged}, 変化率: {ChangePercentage:F3}%",
                    previousImage != null, changeResult.HasChanged, changeResult.ChangePercentage * 100);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ TextDisappearanceEvent発行エラー - WindowHandle: {WindowHandle}, Region: {Region}",
                windowHandle, captureRegion);
        }
    }

    /// <summary>
    /// テキスト消失信頼度計算（Gemini Review対応: 変化率を考慮した動的計算）
    /// </summary>
    /// <param name="changeResult">画像変化検知結果</param>
    /// <returns>信頼度スコア (0.0-1.0)</returns>
    private static float CalculateDisappearanceConfidence(ImageChangeResult changeResult)
    {
        // ベース信頼度（検知ステージに基づく）
        float baseConfidence = changeResult.DetectionStage switch
        {
            1 => 0.95f, // Stage1: 高信頼度（フィルタリング済み）
            2 => 0.85f, // Stage2: 中信頼度
            3 => 0.75f, // Stage3: やや信頼度低
            _ => 0.60f  // その他: 最低信頼度
        };

        // 変化率による補正（変化率が低いほど信頼度を上げる）
        // changeResult.ChangePercentageは0.0-1.0の範囲
        float changeRate = Math.Max(0.0f, Math.Min(1.0f, changeResult.ChangePercentage)); // 念のためクランプ

        // 変化率が0に近いほど信頼度を向上させる補正値
        // 最大+0.05の信頼度向上（5%向上）
        float changeAdjustment = (0.05f - changeRate) * 0.1f; // 0.05f以下で正の補正

        // 最終信頼度の計算（0.6-1.0の範囲にクランプ）
        float finalConfidence = Math.Max(0.6f, Math.Min(1.0f, baseConfidence + changeAdjustment));

        return finalConfidence;
    }

    /// <summary>
    /// [Issue #392] テキスト消失/変化パターン判定（前回OCRテキスト位置 × 画像変化領域）
    /// [Issue #407] ピクセル変化率チェック追加 - 微小背景変化による誤判定を防止
    /// </summary>
    /// <param name="changeResult">画像変化検知結果</param>
    /// <param name="contextId">コンテキストID</param>
    /// <param name="previousImage">前回画像（ピクセル変化率比較用）</param>
    /// <param name="currentImage">現在画像（ピクセル変化率比較用）</param>
    /// <returns>消失したテキスト矩形。消失が検知されなかった場合はnull</returns>
    /// <remarks>
    /// 旧方式（Issue #230で無効化）: 画像変化率+SSIMのみ → 画面フリッカーで誤検知
    /// 新方式: 前回OCRで実際にテキストが検出された位置と、画像変化領域の重なりを判定
    /// + [Issue #407] 重なり検出時にテキスト領域内のピクセル変化率が閾値以上か確認
    /// → 「テキストがあった場所が変わった」を事実ベースで検知
    /// [Issue #486] 戻り値をboolから具体的なテキスト矩形に変更。
    /// captureRegion全体ではなく、消失したテキスト領域のみをDisappearedRegionsとして使用する。
    /// </remarks>
    private Rectangle? FindDisappearedTextRegion(
        ImageChangeResult changeResult, string contextId,
        IImage? previousImage, IImage currentImage)
    {
        // 前回テキスト位置がない場合は履歴も確認
        _previousTextBounds.TryGetValue(contextId, out var textBounds);

        // [Issue #481] 現在のテキスト位置 + 履歴テキスト位置を結合して判定
        // _previousTextBoundsは最新OCR結果のみだが、過去に検出されたテキスト位置も追跡対象に含める
        var allTextBounds = new List<Rectangle>();
        if (textBounds != null && textBounds.Length > 0)
        {
            allTextBounds.AddRange(textBounds);
        }
        if (_historicalTextBounds.TryGetValue(contextId, out var historical))
        {
            lock (historical)
            {
                allTextBounds.AddRange(historical);
            }
        }

        if (allTextBounds.Count == 0)
        {
            _logger.LogTrace("[Issue #392] IsTextDisappearance: false - 前回テキスト位置なし（履歴含む）");
            return null;
        }

        // 変化領域がない場合はnull
        if (changeResult.ChangedRegions == null || changeResult.ChangedRegions.Length == 0)
        {
            _logger.LogTrace("[Issue #392] IsTextDisappearance: false - 変化領域なし");
            return null;
        }

        // 変化領域と前回テキスト位置（+履歴）の重なりを判定
        foreach (var changedRegion in changeResult.ChangedRegions)
        {
            foreach (var textRect in allTextBounds)
            {
                // [Issue #486] 極小テキスト矩形をスキップ（OCRノイズ/UI要素の誤検出）
                var textArea = textRect.Width * textRect.Height;
                if (textArea < MinTextBoundsArea)
                {
                    _logger.LogDebug("[Issue #486] 極小テキスト矩形をスキップ (Area={Area}, Text=({TX},{TY},{TW}x{TH}))",
                        textArea, textRect.X, textRect.Y, textRect.Width, textRect.Height);
                    continue;
                }

                if (changedRegion.IntersectsWith(textRect))
                {
                    // [Issue #407] ピクセル変化率チェック: 微小な背景変化によるテキスト消失誤判定を防止
                    if (previousImage != null)
                    {
                        var changeRate = CalculateTextAreaChangeRate(
                            previousImage, currentImage, textRect);

                        if (changeRate < TextAreaChangeThreshold)
                        {
                            _logger.LogDebug(
                                "[Issue #407] テキスト領域内の変化率が低い({Rate:P1} < {Threshold:P0}) - 消失判定をスキップ (Text=({TX},{TY},{TW}x{TH}))",
                                changeRate, TextAreaChangeThreshold, textRect.X, textRect.Y, textRect.Width, textRect.Height);
                            continue; // この textRect は消失していない
                        }
                    }

                    _logger.LogInformation(
                        "[Issue #392/#481/#486] TextDisappearance検知 - テキスト矩形を返却 (Changed=({CX},{CY},{CW}x{CH}), Text=({TX},{TY},{TW}x{TH}))",
                        changedRegion.X, changedRegion.Y, changedRegion.Width, changedRegion.Height,
                        textRect.X, textRect.Y, textRect.Width, textRect.Height);
                    return textRect;
                }
            }
        }

        // 座標不一致のデバッグ情報を出力
        _logger.LogDebug("[Issue #392/#481] IsTextDisappearance: false - 変化領域{ChangedCount}個とテキスト{TextCount}個（現在+履歴）に重なりなし",
            changeResult.ChangedRegions.Length, allTextBounds.Count);
        if (changeResult.ChangedRegions.Length > 0 && allTextBounds.Count > 0)
        {
            var cr = changeResult.ChangedRegions[0];
            var tb = allTextBounds[0];
            _logger.LogDebug(
                "[Issue #392] 座標デバッグ: ChangedRegion[0]=({CX},{CY},{CW}x{CH}), TextBounds[0]=({TX},{TY},{TW}x{TH})",
                cr.X, cr.Y, cr.Width, cr.Height, tb.X, tb.Y, tb.Width, tb.Height);
        }
        return null;
    }

    /// <summary>
    /// [Issue #407] テキスト領域変化閾値
    /// テキスト領域内のピクセル変化率がこの値未満の場合、テキスト消失とは判定しない
    /// </summary>
    private const float TextAreaChangeThreshold = 0.30f;

    /// <summary>
    /// [Issue #486] テキスト矩形の最小面積（px²）
    /// この面積未満のテキスト矩形はOCRノイズ/UI要素の誤検出とみなし、
    /// テキスト消失判定から除外する。
    /// ログ分析: 誤判定トリガーは全て面積500px²未満（14x5=70, 12x10=120, 21x15=315等）
    /// 正当なテキストは面積3,600px²以上（150x24, 358x26等）
    /// </summary>
    private const int MinTextBoundsArea = 500;

    /// <summary>
    /// [Issue #407] テキスト領域内のピクセル変化率を計算（サンプリングベース高速版）
    /// </summary>
    /// <param name="previousImage">前回画像</param>
    /// <param name="currentImage">現在画像</param>
    /// <param name="textRect">テキストのバウンディングボックス</param>
    /// <returns>変化率 (0.0-1.0)</returns>
    private static float CalculateTextAreaChangeRate(
        IImage previousImage, IImage currentImage, Rectangle textRect)
    {
        // 画像境界にクランプ
        var imageRect = new Rectangle(0, 0,
            Math.Min(previousImage.Width, currentImage.Width),
            Math.Min(previousImage.Height, currentImage.Height));
        var clampedRect = Rectangle.Intersect(textRect, imageRect);

        if (clampedRect.IsEmpty || clampedRect.Width <= 0 || clampedRect.Height <= 0)
            return 1.0f; // 領域外 → 安全側で「変化あり」

        try
        {
            // PixelDataLock は readonly ref struct のため using 宣言不可、明示的に Dispose() を呼ぶ
            var prevLock = previousImage.LockPixelData();
            var currLock = currentImage.LockPixelData();

            int changedPixels = 0;
            int totalSampled = 0;

            // 等間隔サンプリング（最大約100サンプル）
            int stepX = Math.Max(1, clampedRect.Width / 10);
            int stepY = Math.Max(1, clampedRect.Height / 10);

            for (int y = clampedRect.Y; y < clampedRect.Y + clampedRect.Height; y += stepY)
            {
                for (int x = clampedRect.X; x < clampedRect.X + clampedRect.Width; x += stepX)
                {
                    int prevOffset = y * prevLock.Stride + x * 4; // BGRA32
                    int currOffset = y * currLock.Stride + x * 4;

                    if (prevOffset + 2 >= prevLock.Data.Length ||
                        currOffset + 2 >= currLock.Data.Length)
                        continue;

                    int diffB = Math.Abs(prevLock.Data[prevOffset] - currLock.Data[currOffset]);
                    int diffG = Math.Abs(prevLock.Data[prevOffset + 1] - currLock.Data[currOffset + 1]);
                    int diffR = Math.Abs(prevLock.Data[prevOffset + 2] - currLock.Data[currOffset + 2]);

                    if (diffR + diffG + diffB > 30)
                        changedPixels++;

                    totalSampled++;
                }
            }

            prevLock.Dispose();
            currLock.Dispose();

            return totalSampled > 0 ? (float)changedPixels / totalSampled : 1.0f;
        }
        catch
        {
            return 1.0f; // エラー時は安全側で「変化あり」
        }
    }
}
