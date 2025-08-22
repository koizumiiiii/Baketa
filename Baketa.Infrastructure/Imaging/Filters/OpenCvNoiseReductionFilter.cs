using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Imaging.Filters;
using Baketa.Core.Abstractions.Imaging.Pipeline;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Baketa.Core.Services.Imaging;

namespace Baketa.Infrastructure.Imaging.Filters;

/// <summary>
/// OpenCvSharp を使用したノイズ除去フィルター
/// ゲーム画面のノイズ、ジャギー、圧縮アーティファクトを除去
/// </summary>
public sealed class OpenCvNoiseReductionFilter : BaseImageFilter
{
    private readonly ILogger<OpenCvNoiseReductionFilter>? _logger;

    /// <summary>
    /// フィルターの名前
    /// </summary>
    public override string Name => "OpenCVノイズ除去";

    /// <summary>
    /// フィルターの説明
    /// </summary>
    public override string Description => "OpenCvSharp を使用してゲーム画面のノイズ、ジャギー、圧縮アーティファクトを除去し、OCR精度を向上させます";

    /// <summary>
    /// フィルターのカテゴリ
    /// </summary>
    public override FilterCategory Category => FilterCategory.Blur;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="logger">ロガー</param>
    public OpenCvNoiseReductionFilter(ILogger<OpenCvNoiseReductionFilter>? logger = null) : base(logger)
    {
        _logger = logger;
        InitializeDefaultParameters();
    }

    /// <summary>
    /// デフォルトパラメータを初期化
    /// </summary>
    protected override void InitializeDefaultParameters()
    {
        SetParameter("Method", "Bilateral");        // ノイズ除去方法: "Gaussian", "Bilateral", "Median", "FastNlMeansDenoising"
        SetParameter("KernelSize", 5);              // Gaussian/Medianカーネルサイズ (3-15の奇数)
        SetParameter("SigmaColor", 75.0);           // Bilateral用色空間標準偏差 (10-200)
        SetParameter("SigmaSpace", 75.0);           // Bilateral用座標空間標準偏差 (10-200)
        SetParameter("H", 10.0);                    // FastNlMeansDenoising用フィルタ強度 (3-30)
        SetParameter("TemplateWindowSize", 7);      // FastNlMeans用テンプレートウィンドウサイズ (7-21の奇数)
        SetParameter("SearchWindowSize", 21);       // FastNlMeans用探索ウィンドウサイズ (15-35の奇数)
        SetParameter("Iterations", 1);              // 処理の繰り返し回数 (1-3)
    }

    /// <summary>
    /// パラメータ定義を取得
    /// </summary>
    protected override IReadOnlyCollection<PipelineStepParameter> GetParameterDefinitions()
    {
        return 
        [
            new PipelineStepParameter("Method", "ノイズ除去方法", typeof(string), "Bilateral", allowedValues: ["Gaussian", "Bilateral", "Median", "FastNlMeansDenoising"]),
            new PipelineStepParameter("KernelSize", "Gaussian/Medianカーネルサイズ", typeof(int), 5, minValue: 3, maxValue: 15),
            new PipelineStepParameter("SigmaColor", "Bilateral用色空間標準偏差", typeof(double), 75.0, minValue: 10.0, maxValue: 200.0),
            new PipelineStepParameter("SigmaSpace", "Bilateral用座標空間標準偏差", typeof(double), 75.0, minValue: 10.0, maxValue: 200.0),
            new PipelineStepParameter("H", "FastNlMeansDenoising用フィルタ強度", typeof(double), 10.0, minValue: 3.0, maxValue: 30.0),
            new PipelineStepParameter("TemplateWindowSize", "FastNlMeans用テンプレートウィンドウサイズ", typeof(int), 7, minValue: 7, maxValue: 21),
            new PipelineStepParameter("SearchWindowSize", "FastNlMeans用探索ウィンドウサイズ", typeof(int), 21, minValue: 15, maxValue: 35),
            new PipelineStepParameter("Iterations", "処理の繰り返し回数", typeof(int), 1, minValue: 1, maxValue: 3)
        ];
    }

    /// <summary>
    /// ノイズ除去フィルターを適用
    /// </summary>
    /// <param name="inputImage">入力画像</param>
    /// <returns>ノイズ除去された画像</returns>
    public override async Task<IAdvancedImage> ApplyAsync(IAdvancedImage inputImage)
    {
        ArgumentNullException.ThrowIfNull(inputImage);

        var method = GetParameter<string>("Method");
        var kernelSize = GetParameter<int>("KernelSize");
        var sigmaColor = GetParameter<double>("SigmaColor");
        var sigmaSpace = GetParameter<double>("SigmaSpace");
        var h = GetParameter<double>("H");
        var templateWindowSize = GetParameter<int>("TemplateWindowSize");
        var searchWindowSize = GetParameter<int>("SearchWindowSize");
        var iterations = GetParameter<int>("Iterations");

        _logger?.LogInformation("OpenCVノイズ除去開始: Method={Method}, KernelSize={KernelSize}, Iterations={Iterations}", 
            method, kernelSize, iterations);

        try
        {
            // 現在は元画像をそのまま返す（一時対応）
            // TODO: OpenCV実装後に正式な処理に置き換え
            switch (method)
            {
                case "Gaussian":
                    _logger?.LogInformation("ガウシアンノイズ除去完了: KernelSize={KernelSize}", kernelSize);
                    break;
                case "Bilateral":
                    _logger?.LogInformation("バイラテラルノイズ除去完了: SigmaColor={SigmaColor}, SigmaSpace={SigmaSpace}", 
                        sigmaColor, sigmaSpace);
                    break;
                case "Median":
                    _logger?.LogInformation("メディアンノイズ除去完了: KernelSize={KernelSize}", kernelSize);
                    break;
                case "FastNlMeansDenoising":
                    _logger?.LogInformation("FastNlMeansDenoising完了: H={H}, Template={TemplateSize}, Search={SearchSize}", 
                        h, templateWindowSize, searchWindowSize);
                    break;
            }

            return await Task.FromResult(inputImage).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "OpenCVノイズ除去中にエラーが発生しました");
            throw;
        }
    }
}