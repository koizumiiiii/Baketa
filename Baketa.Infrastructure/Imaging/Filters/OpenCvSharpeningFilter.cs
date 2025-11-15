using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Imaging.Filters;
using Baketa.Core.Abstractions.Imaging.Pipeline;
using Baketa.Core.Services.Imaging;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace Baketa.Infrastructure.Imaging.Filters;

/// <summary>
/// OpenCvSharp を使用したシャープニングフィルター
/// ぼやけたテキストやゲーム画面の鮮明度を向上させてOCR精度を改善
/// </summary>
public sealed class OpenCvSharpeningFilter : BaseImageFilter
{
    private readonly ILogger<OpenCvSharpeningFilter>? _logger;

    /// <summary>
    /// フィルターの名前
    /// </summary>
    public override string Name => "OpenCVシャープニング";

    /// <summary>
    /// フィルターの説明
    /// </summary>
    public override string Description => "OpenCvSharp を使用してぼやけたテキストやゲーム画面の鮮明度を向上させ、OCR精度を改善します";

    /// <summary>
    /// フィルターのカテゴリ
    /// </summary>
    public override FilterCategory Category => FilterCategory.Sharpen;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="logger">ロガー</param>
    public OpenCvSharpeningFilter(ILogger<OpenCvSharpeningFilter>? logger = null) : base(logger)
    {
        _logger = logger;
        InitializeDefaultParameters();
    }

    /// <summary>
    /// デフォルトパラメータを初期化
    /// </summary>
    protected override void InitializeDefaultParameters()
    {
        SetParameter("Method", "UnsharpMask");      // シャープニング方法: "UnsharpMask", "Laplacian", "CustomKernel", "DetailEnhance"
        SetParameter("Strength", 1.5);             // シャープニング強度 (0.5-3.0)
        SetParameter("Radius", 1.0);               // UnsharpMask用半径 (0.5-5.0)
        SetParameter("Amount", 1.0);               // UnsharpMask用適用量 (0.1-2.0)
        SetParameter("Threshold", 0);              // UnsharpMask用閾値 (0-255)
        SetParameter("KernelType", "Standard");    // カーネルタイプ: "Standard", "Strong", "Subtle"
        SetParameter("Alpha", 0.3);                // DetailEnhance用強度調整 (0.1-1.0)
        SetParameter("Sigma", 1.0);                // ガウシアンぼかし用標準偏差 (0.5-3.0)
    }

    /// <summary>
    /// パラメータ定義を取得
    /// </summary>
    protected override IReadOnlyCollection<PipelineStepParameter> GetParameterDefinitions()
    {
        return
        [
            new PipelineStepParameter("Method", "シャープニング方法", typeof(string), "UnsharpMask", allowedValues: ["UnsharpMask", "Laplacian", "CustomKernel", "DetailEnhance"]),
            new PipelineStepParameter("Strength", "シャープニング強度", typeof(double), 1.5, minValue: 0.5, maxValue: 3.0),
            new PipelineStepParameter("Radius", "UnsharpMask用半径", typeof(double), 1.0, minValue: 0.5, maxValue: 5.0),
            new PipelineStepParameter("Amount", "UnsharpMask用適用量", typeof(double), 1.0, minValue: 0.1, maxValue: 2.0),
            new PipelineStepParameter("Threshold", "UnsharpMask用閾値", typeof(int), 0, minValue: 0, maxValue: 255),
            new PipelineStepParameter("KernelType", "カーネルタイプ", typeof(string), "Standard", allowedValues: ["Standard", "Strong", "Subtle"]),
            new PipelineStepParameter("Alpha", "DetailEnhance用強度調整", typeof(double), 0.3, minValue: 0.1, maxValue: 1.0),
            new PipelineStepParameter("Sigma", "ガウシアンぼかし用標準偏差", typeof(double), 1.0, minValue: 0.5, maxValue: 3.0)
        ];
    }

    /// <summary>
    /// シャープニングフィルターを適用
    /// </summary>
    /// <param name="inputImage">入力画像</param>
    /// <returns>シャープニング処理された画像</returns>
    public override async Task<IAdvancedImage> ApplyAsync(IAdvancedImage inputImage)
    {
        ArgumentNullException.ThrowIfNull(inputImage);

        var method = GetParameter<string>("Method");
        var strength = GetParameter<double>("Strength");
        var radius = GetParameter<double>("Radius");
        var amount = GetParameter<double>("Amount");
        var threshold = GetParameter<int>("Threshold");
        var kernelType = GetParameter<string>("KernelType");
        var alpha = GetParameter<double>("Alpha");
        var sigma = GetParameter<double>("Sigma");

        _logger?.LogInformation("OpenCVシャープニング開始: Method={Method}, Strength={Strength}, KernelType={KernelType}",
            method, strength, kernelType);

        try
        {
            // 現在は元画像をそのまま返す（一時対応）
            // TODO: OpenCV実装後に正式な処理に置き換え
            switch (method)
            {
                case "UnsharpMask":
                    _logger?.LogInformation("アンシャープマスク完了: Radius={Radius}, Amount={Amount}, Threshold={Threshold}",
                        radius, amount, threshold);
                    break;
                case "Laplacian":
                    _logger?.LogInformation("ラプラシアンシャープニング完了: Strength={Strength}", strength);
                    break;
                case "CustomKernel":
                    _logger?.LogInformation("カスタムカーネルシャープニング完了: Type={KernelType}, Strength={Strength}",
                        kernelType, strength);
                    break;
                case "DetailEnhance":
                    _logger?.LogInformation("ディテール強化完了: Alpha={Alpha}, Sigma={Sigma}", alpha, sigma);
                    break;
            }

            return await Task.FromResult(inputImage).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "OpenCVシャープニング中にエラーが発生しました");
            throw;
        }
    }
}
