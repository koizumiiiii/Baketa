using System.Collections.Generic;

namespace Baketa.Core.Abstractions.Imaging.Pipeline;

/// <summary>
/// フィルターファクトリーインターフェース
/// </summary>
public interface IFilterFactory
{
    /// <summary>
    /// タイプ名からフィルターを生成します
    /// </summary>
    /// <param name="typeName">フィルタータイプ名</param>
    /// <returns>生成されたフィルター、または生成できない場合はnull</returns>
    IImageFilter? CreateFilter(string typeName);

    /// <summary>
    /// タイプ名とパラメータからフィルターを生成します
    /// </summary>
    /// <param name="typeName">フィルタータイプ名</param>
    /// <param name="parameters">フィルターパラメータ</param>
    /// <returns>生成されたフィルター、または生成できない場合はnull</returns>
    IImageFilter? CreateFilter(string typeName, IDictionary<string, object> parameters);

    /// <summary>
    /// 利用可能なすべてのフィルタータイプを取得します
    /// </summary>
    /// <returns>フィルタータイプ名のリスト</returns>
    IEnumerable<string> GetAvailableFilterTypes();

    /// <summary>
    /// 指定されたカテゴリのフィルタータイプを取得します
    /// </summary>
    /// <param name="category">フィルターカテゴリ</param>
    /// <returns>フィルタータイプ名のリスト</returns>
    IEnumerable<string> GetFilterTypesByCategory(FilterCategory category);
}
