using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Microsoft.Extensions.Logging;
using OCRTextRegion = Baketa.Core.Abstractions.OCR.TextDetection.TextRegion;

namespace Baketa.Core.Abstractions.OCR.TextDetection
{
    /// <summary>
    /// テキスト領域検出の基本実装クラス
    /// </summary>
    public abstract class TextRegionDetectorBase : ITextRegionDetector
    {
        /// <summary>
        /// パラメータを格納するディクショナリ
        /// </summary>
        private readonly Dictionary<string, object> _parameters = new Dictionary<string, object>();
        
        /// <summary>
        /// ロガーを取得します
        /// </summary>
        protected ILogger? Logger { get; }
        
        /// <summary>
        /// パラメータディクショナリを取得します
        /// </summary>
        protected Dictionary<string, object> Parameters { get; }
        
        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="logger">ロガー</param>
        protected TextRegionDetectorBase(ILogger? logger = null)
        {
            Logger = logger;
            Parameters = _parameters;
        }
        
        /// <summary>
        /// 検出器の名前
        /// </summary>
        public abstract string Name { get; }
        
        /// <summary>
        /// 検出器の説明
        /// </summary>
        public abstract string Description { get; }
        
        /// <summary>
        /// 検出に使用するアルゴリズム
        /// </summary>
        public abstract TextDetectionMethod Method { get; }
        
        /// <summary>
        /// 画像からテキスト領域を検出します
        /// </summary>
        /// <param name="image">検出対象の画像</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>検出されたテキスト領域のリスト</returns>
        public abstract Task<IReadOnlyList<OCRTextRegion>> DetectRegionsAsync(
            IAdvancedImage image, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// 検出器のパラメータを設定します
        /// </summary>
        /// <param name="parameterName">パラメータ名</param>
        /// <param name="value">設定値</param>
        public virtual void SetParameter(string parameterName, object value)
        {
            ArgumentNullException.ThrowIfNull(parameterName, nameof(parameterName));
            _parameters[parameterName] = value;
        }
        
        /// <summary>
        /// 検出器のパラメータを取得します
        /// </summary>
        /// <param name="parameterName">パラメータ名</param>
        /// <returns>パラメータ値</returns>
        public virtual object GetParameter(string parameterName)
        {
            ArgumentNullException.ThrowIfNull(parameterName, nameof(parameterName));
            
            if (_parameters.TryGetValue(parameterName, out var value))
            {
                return value;
            }
            
            throw new KeyNotFoundException($"パラメータが見つかりません: {parameterName}");
        }
        
        /// <summary>
        /// 指定した型でパラメータを取得します
        /// </summary>
        /// <typeparam name="T">取得する型</typeparam>
        /// <param name="parameterName">パラメータ名</param>
        /// <returns>型変換されたパラメータ値</returns>
        public virtual T GetParameter<T>(string parameterName)
        {
            var value = GetParameter(parameterName);
            
            try
            {
                return (T)Convert.ChangeType(value, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (InvalidCastException ex)
            {
                throw new InvalidCastException(
                    $"パラメータ {parameterName} を型 {typeof(T).Name} に変換できません", ex);
            }
        }
        
        /// <summary>
        /// すべてのパラメータを取得します
        /// </summary>
        /// <returns>パラメータディクショナリ</returns>
        public virtual IReadOnlyDictionary<string, object> GetParameters()
        {
            return new Dictionary<string, object>(_parameters);
        }
        
        /// <summary>
        /// 検出器の現在の設定をプロファイルとして保存します
        /// </summary>
        /// <param name="profileName">プロファイル名</param>
        /// <returns>非同期タスク</returns>
        public virtual Task SaveProfileAsync(string profileName)
        {
            // 実装はサブクラスに任せる
            return Task.CompletedTask;
        }
        
        /// <summary>
        /// プロファイルから検出器の設定を読み込みます
        /// </summary>
        /// <param name="profileName">プロファイル名</param>
        /// <returns>非同期タスク</returns>
        public virtual Task LoadProfileAsync(string profileName)
        {
            // 実装はサブクラスに任せる
            return Task.CompletedTask;
        }
    }
}