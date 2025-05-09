using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Baketa.Core.Abstractions.Imaging
{
    /// <summary>
    /// 画像フィルターを表すインターフェース
    /// </summary>
    public interface IImageFilter
    {
        /// <summary>
        /// フィルターの名前
        /// </summary>
        string Name { get; }
        
        /// <summary>
        /// フィルターの説明
        /// </summary>
        string Description { get; }
        
        /// <summary>
        /// フィルターのカテゴリ
        /// </summary>
        FilterCategory Category { get; }
        
        /// <summary>
        /// 画像にフィルターを適用します
        /// </summary>
        /// <param name="inputImage">入力画像</param>
        /// <returns>フィルター適用後の新しい画像</returns>
        Task<IAdvancedImage> ApplyAsync(IAdvancedImage inputImage);
        
        /// <summary>
        /// フィルターのパラメータをリセットします
        /// </summary>
        void ResetParameters();
        
        /// <summary>
        /// フィルターの現在のパラメータを取得します
        /// </summary>
        /// <returns>パラメータディクショナリ</returns>
        IDictionary<string, object> GetParameters();
        
        /// <summary>
        /// フィルターのパラメータを設定します
        /// </summary>
        /// <param name="name">パラメータ名</param>
        /// <param name="value">パラメータ値</param>
        void SetParameter(string name, object value);
        
        /// <summary>
        /// 指定された画像フォーマットに対応しているかを確認します
        /// </summary>
        /// <param name="format">確認する画像フォーマット</param>
        /// <returns>対応している場合はtrue、そうでない場合はfalse</returns>
        bool SupportsFormat(ImageFormat format);
        
        /// <summary>
        /// フィルター適用後の画像情報を取得します
        /// </summary>
        /// <param name="inputImage">入力画像</param>
        /// <returns>出力画像の情報</returns>
        ImageInfo GetOutputImageInfo(IAdvancedImage inputImage);
    }
}