using System.Threading.Tasks;

namespace Baketa.Core.Abstractions.Imaging;

/// <summary>
/// 異なる画像表現間の変換を行うインターフェース
/// </summary>
/// <typeparam name="TSource">変換元の画像型</typeparam>
/// <typeparam name="TTarget">変換先の画像型</typeparam>
public interface IImageConverter<TSource, TTarget>
{
    /// <summary>
    /// ソース画像を対象の形式に変換します。
    /// </summary>
    /// <param name="source">変換元の画像</param>
    /// <returns>変換された画像</returns>
    Task<TTarget> ConvertAsync(TSource source);

    /// <summary>
    /// 指定されたソース画像を変換できるかどうかを確認します。
    /// </summary>
    /// <param name="source">検証する画像</param>
    /// <returns>変換可能な場合はtrue、そうでない場合はfalse</returns>
    bool CanConvert(TSource source);
}

/// <summary>
/// 画像変換器のファクトリーインターフェース
/// </summary>
public interface IImageConverterFactory
{
    /// <summary>
    /// 指定された型の変換に対応した画像変換器を取得します。
    /// </summary>
    /// <typeparam name="TSource">変換元の画像型</typeparam>
    /// <typeparam name="TTarget">変換先の画像型</typeparam>
    /// <returns>画像変換器のインスタンス</returns>
    IImageConverter<TSource, TTarget> GetConverter<TSource, TTarget>();

    /// <summary>
    /// 指定された型の変換に対応した画像変換器が存在するか確認します。
    /// </summary>
    /// <typeparam name="TSource">変換元の画像型</typeparam>
    /// <typeparam name="TTarget">変換先の画像型</typeparam>
    /// <returns>変換器が存在する場合はtrue、そうでない場合はfalse</returns>
    bool HasConverter<TSource, TTarget>();
}
