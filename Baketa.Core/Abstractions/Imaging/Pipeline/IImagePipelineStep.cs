using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Baketa.Core.Abstractions.Imaging.Pipeline;

/// <summary>
/// パイプラインの個別処理ステップを表すインターフェース
/// </summary>
public interface IImagePipelineStep
{
    /// <summary>
    /// ステップの名前
    /// </summary>
    string Name { get; }

    /// <summary>
    /// ステップの説明
    /// </summary>
    string Description { get; }

    /// <summary>
    /// ステップのパラメータ定義
    /// </summary>
    IReadOnlyCollection<PipelineStepParameter> Parameters { get; }

    /// <summary>
    /// ステップのエラーハンドリング戦略
    /// </summary>
    StepErrorHandlingStrategy ErrorHandlingStrategy { get; set; }

    /// <summary>
    /// ステップを実行します
    /// </summary>
    /// <param name="input">入力画像</param>
    /// <param name="context">パイプライン実行コンテキスト</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>処理結果画像</returns>
    Task<IAdvancedImage> ExecuteAsync(IAdvancedImage input, PipelineContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// パラメータ値を設定します
    /// </summary>
    /// <param name="parameterName">パラメータ名</param>
    /// <param name="value">設定する値</param>
    void SetParameter(string parameterName, object value);

    /// <summary>
    /// パラメータ値を取得します
    /// </summary>
    /// <param name="parameterName">パラメータ名</param>
    /// <returns>パラメータ値</returns>
    object GetParameter(string parameterName);

    /// <summary>
    /// パラメータ値をジェネリック型で取得します
    /// </summary>
    /// <typeparam name="T">取得する型</typeparam>
    /// <param name="parameterName">パラメータ名</param>
    /// <returns>パラメータ値</returns>
    T GetParameter<T>(string parameterName);

    /// <summary>
    /// 出力画像情報を取得します
    /// </summary>
    /// <param name="input">入力画像</param>
    /// <returns>出力画像の情報</returns>
    PipelineImageInfo GetOutputImageInfo(IAdvancedImage input);
}
