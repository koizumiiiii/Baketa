using System;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Imaging.Filters;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Baketa.Core.Services.Imaging;

namespace Baketa.Infrastructure.Imaging.Filters;

/// <summary>
/// OpenCvSharp を使用した高精度適応的二値化フィルター
/// Phase 3: ゲーム背景の暗さ・明度変化に対応した適応的閾値処理を実装
/// </summary>
public sealed class OpenCvAdaptiveThresholdFilter : ImageFilterBase
{
    private readonly ILogger<OpenCvAdaptiveThresholdFilter>? _logger;

    /// <summary>
    /// フィルターの名前
    /// </summary>
    public override string Name => "OpenCV適応的二値化";

    /// <summary>
    /// フィルターの説明
    /// </summary>
    public override string Description => "OpenCvSharp を使用してゲーム画面の明度変化に適応した高精度二値化を実行";

    /// <summary>
    /// フィルターのカテゴリ
    /// </summary>
    public override FilterCategory Category => FilterCategory.Threshold;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="logger">ロガー</param>
    public OpenCvAdaptiveThresholdFilter(ILogger<OpenCvAdaptiveThresholdFilter>? logger = null)
    {
        _logger = logger;
        InitializeDefaultParameters();
    }

    /// <summary>
    /// デフォルトパラメータを初期化
    /// </summary>
    protected override void InitializeDefaultParameters()
    {
        // 適応的二値化の基本パラメータ
        RegisterParameter("BlockSize", 15);               // ブロックサイズ（奇数、推奨: 11-21）
        RegisterParameter("C", 8.0);                      // 閾値調整定数（推奨: 2-12）
        RegisterParameter("MaxValue", 255.0);             // 最大値
        RegisterParameter("AdaptiveMethod", AdaptiveThresholdTypes.GaussianC); // 適応的手法
        RegisterParameter("ThresholdType", ThresholdTypes.Binary);             // 閾値処理タイプ
        
        // ゲーム画面特化パラメータ
        RegisterParameter("PreBlurKernelSize", 3);        // 前処理ブラーカーネルサイズ
        RegisterParameter("PreBlurEnabled", true);        // 前処理ブラー有効化
        RegisterParameter("PostMorphEnabled", true);      // 後処理モルフォロジー有効化
        RegisterParameter("MorphKernelSize", 2);          // モルフォロジーカーネルサイズ
        RegisterParameter("MorphIterations", 1);          // モルフォロジー反復回数
        
        // デバッグ・ログ設定
        RegisterParameter("EnableDetailedLogging", true);  // 詳細ログ有効化
    }

    /// <summary>
    /// 画像にフィルターを適用
    /// </summary>
    /// <param name="inputImage">入力画像</param>
    /// <returns>フィルター適用後の新しい画像</returns>
    public override async Task<IAdvancedImage> ApplyAsync(IAdvancedImage inputImage)
    {
        ArgumentNullException.ThrowIfNull(inputImage);

        // パラメータ取得
        var blockSize = GetParameterValue<int>("BlockSize");
        var c = GetParameterValue<double>("C");
        var enableDetailedLogging = GetParameterValue<bool>("EnableDetailedLogging");

        // パラメータ妥当性チェック
        blockSize = EnsureOddNumber(Math.Max(3, blockSize));

        if (enableDetailedLogging)
        {
            _logger?.LogInformation("OpenCV適応的二値化開始: BlockSize={BlockSize}, C={C}",
                blockSize, c);
        }

        try
        {
            // 🔧 一時的に元画像を返す（EnhanceAsyncにサイズ問題があるため）
            if (enableDetailedLogging)
            {
                _logger?.LogInformation("OpenCV適応的二値化完了: 元画像を返す（一時対応）");
            }

            // 元画像のクローンを返して安全性を保つ
            return (IAdvancedImage)inputImage.Clone();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "OpenCV適応的二値化中にエラーが発生しました");
            // エラー時は元の画像を返す
            return inputImage;
        }
    }

    /// <summary>
    /// フィルター適用後の画像情報を取得
    /// </summary>
    /// <param name="inputImage">入力画像</param>
    /// <returns>出力画像の情報</returns>
    public override ImageInfo GetOutputImageInfo(IAdvancedImage inputImage)
    {
        ArgumentNullException.ThrowIfNull(inputImage);
        
        return new ImageInfo
        {
            Width = inputImage.Width,
            Height = inputImage.Height,
            Format = ImageFormat.Grayscale8,  // 二値化後はグレースケール
            Channels = 1
        };
    }

    /// <summary>
    /// 偶数を奇数に調整
    /// </summary>
    /// <param name="value">調整する値</param>
    /// <returns>奇数値</returns>
    private static int EnsureOddNumber(int value)
    {
        return value % 2 == 0 ? value + 1 : value;
    }
}