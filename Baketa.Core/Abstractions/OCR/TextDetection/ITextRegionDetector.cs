using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using OCRTextRegion = Baketa.Core.Abstractions.OCR.TextDetection.TextRegion;

namespace Baketa.Core.Abstractions.OCR.TextDetection;

    /// <summary>
    /// テキスト領域検出インターフェース
    /// </summary>
    public interface ITextRegionDetector
    {
        /// <summary>
        /// 検出器の名前
        /// </summary>
        string Name { get; }
        
        /// <summary>
        /// 検出器の説明
        /// </summary>
        string Description { get; }
        
        /// <summary>
        /// 検出に使用するアルゴリズム
        /// </summary>
        TextDetectionMethod Method { get; }
        
        /// <summary>
        /// 画像からテキスト領域を検出します
        /// </summary>
        /// <param name="image">検出対象の画像</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>検出されたテキスト領域のリスト</returns>
        Task<IReadOnlyList<OCRTextRegion>> DetectRegionsAsync(IAdvancedImage image, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// 検出器のパラメータを設定します
        /// </summary>
        /// <param name="parameterName">パラメータ名</param>
        /// <param name="value">設定値</param>
        void SetParameter(string parameterName, object value);
        
        /// <summary>
        /// 検出器のパラメータを取得します
        /// </summary>
        /// <param name="parameterName">パラメータ名</param>
        /// <returns>パラメータ値</returns>
        object GetParameter(string parameterName);
        
        /// <summary>
        /// 指定した型でパラメータを取得します
        /// </summary>
        /// <typeparam name="T">取得する型</typeparam>
        /// <param name="parameterName">パラメータ名</param>
        /// <returns>型変換されたパラメータ値</returns>
        T GetParameter<T>(string parameterName);
        
        /// <summary>
        /// すべてのパラメータを取得します
        /// </summary>
        /// <returns>パラメータディクショナリ</returns>
        IReadOnlyDictionary<string, object> GetParameters();
        
        /// <summary>
        /// 検出器の現在の設定をプロファイルとして保存します
        /// </summary>
        /// <param name="profileName">プロファイル名</param>
        /// <returns>非同期タスク</returns>
        Task SaveProfileAsync(string profileName);
        
        /// <summary>
        /// プロファイルから検出器の設定を読み込みます
        /// </summary>
        /// <param name="profileName">プロファイル名</param>
        /// <returns>非同期タスク</returns>
        Task LoadProfileAsync(string profileName);
    }
