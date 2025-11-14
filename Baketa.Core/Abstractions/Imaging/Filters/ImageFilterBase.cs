using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Baketa.Core.Abstractions.Imaging.Filters;

/// <summary>
/// 画像フィルターの基底クラス
/// </summary>
public abstract class ImageFilterBase : IImageFilter
{
    private readonly Dictionary<string, object> _parameters = [];

    /// <summary>
    /// フィルターの名前
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    /// フィルターの説明
    /// </summary>
    public abstract string Description { get; }

    /// <summary>
    /// フィルターのカテゴリ
    /// </summary>
    public abstract FilterCategory Category { get; }

    /// <summary>
    /// 画像にフィルターを適用します
    /// </summary>
    /// <param name="inputImage">入力画像</param>
    /// <returns>フィルター適用後の新しい画像</returns>
    public abstract Task<IAdvancedImage> ApplyAsync(IAdvancedImage inputImage);

    /// <summary>
    /// フィルターのパラメータをリセットします
    /// </summary>
    public virtual void ResetParameters()
    {
        _parameters.Clear();
        InitializeDefaultParameters();
    }

    /// <summary>
    /// フィルターの現在のパラメータを取得します
    /// </summary>
    /// <returns>パラメータディクショナリ</returns>
    public IDictionary<string, object> GetParameters()
    {
        return new Dictionary<string, object>(_parameters);
    }

    /// <summary>
    /// フィルターのパラメータを設定します
    /// </summary>
    /// <param name="name">パラメータ名</param>
    /// <param name="value">パラメータ値</param>
    public virtual void SetParameter(string name, object value)
    {
        if (!_parameters.ContainsKey(name))
            throw new ArgumentException($"パラメータ '{name}' はこのフィルターでは定義されていません。");

        _parameters[name] = value;
    }

    /// <summary>
    /// 指定された画像フォーマットに対応しているかを確認します
    /// </summary>
    /// <param name="format">確認する画像フォーマット</param>
    /// <returns>対応している場合はtrue、そうでない場合はfalse</returns>
    public virtual bool SupportsFormat(ImageFormat format)
    {
        // 基本実装ではすべてのフォーマットに対応
        return true;
    }

    /// <summary>
    /// フィルター適用後の画像情報を取得します
    /// </summary>
    /// <param name="inputImage">入力画像</param>
    /// <returns>出力画像の情報</returns>
    public virtual ImageInfo GetOutputImageInfo(IAdvancedImage inputImage)
    {
        // デフォルトでは入力画像と同じ情報を返す
        return ImageInfo.FromImage(inputImage);
    }

    /// <summary>
    /// パラメータ値を取得します
    /// </summary>
    /// <param name="name">パラメータ名</param>
    /// <returns>パラメータ値</returns>
    protected object GetParameterValue(string name)
    {
        if (!_parameters.TryGetValue(name, out var value))
            throw new ArgumentException($"パラメータ '{name}' はこのフィルターでは定義されていません。");

        return value;
    }

    /// <summary>
    /// 型指定したパラメータ値を取得します
    /// </summary>
    /// <typeparam name="T">パラメータの型</typeparam>
    /// <param name="name">パラメータ名</param>
    /// <returns>型変換されたパラメータ値</returns>
    protected T GetParameterValue<T>(string name)
    {
        var value = GetParameterValue(name);
        if (value is T typedValue)
            return typedValue;

        throw new InvalidCastException($"パラメータ '{name}' は型 {typeof(T).Name} に変換できません。");
    }

    /// <summary>
    /// パラメータを登録します
    /// </summary>
    /// <param name="name">パラメータ名</param>
    /// <param name="defaultValue">デフォルト値</param>
    protected void RegisterParameter(string name, object defaultValue)
    {
        _parameters[name] = defaultValue;
    }

    /// <summary>
    /// デフォルトパラメータを初期化します
    /// </summary>
    protected abstract void InitializeDefaultParameters();
}
