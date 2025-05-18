using System;
using System.Collections.Generic;
using System.Globalization;

namespace Baketa.Core.Abstractions.OCR;

    /// <summary>
    /// テキスト検出パラメータ
    /// </summary>
    public class TextDetectionParams
    {
        /// <summary>
        /// パラメータのディクショナリ
        /// </summary>
        public Dictionary<string, object> Parameters { get; set; } = new Dictionary<string, object>();
        
        /// <summary>
        /// 検出方法
        /// </summary>
        public TextDetectionMethod Method { get; set; }
        
        // テキスト領域の基本なパラメータ
        
        /// <summary>
        /// 最小幅
        /// </summary>
        public int MinWidth => GetOrDefault<int>("MinWidth", 10);
        
        /// <summary>
        /// 最小高さ
        /// </summary>
        public int MinHeight => GetOrDefault<int>("MinHeight", 10);
        
        /// <summary>
        /// 最小アスペクト比
        /// </summary>
        public float MinAspectRatio => GetOrDefault<float>("MinAspectRatio", 0.1f);
        
        /// <summary>
        /// 最大アスペクト比
        /// </summary>
        public float MaxAspectRatio => GetOrDefault<float>("MaxAspectRatio", 10.0f);
        
        /// <summary>
        /// 統合維持閾値
        /// </summary>
        public float MergeThreshold => GetOrDefault<float>("MergeThreshold", 0.5f);
        
        // MSER特有パラメータ
        
        /// <summary>
        /// MSERのDeltaパラメータ
        /// </summary>
        public int MserDelta => GetOrDefault<int>("delta", 5);
        
        /// <summary>
        /// MSERの最小領域サイズ
        /// </summary>
        public int MserMinArea => GetOrDefault<int>("minArea", 60);
        
        /// <summary>
        /// MSERの最大領域サイズ
        /// </summary>
        public int MserMaxArea => GetOrDefault<int>("maxArea", 14400);
        
        // SWT特有パラメータ
        
        /// <summary>
        /// 暗い背景上の明るいテキストかどうか
        /// </summary>
        public bool DarkTextOnLight => GetOrDefault<bool>("darkTextOnLight", true);
        
        /// <summary>
        /// ストローク幅比率
        /// </summary>
        public float StrokeWidthRatio => GetOrDefault<float>("strokeWidthRatio", 3.0f);
        
        /// <summary>
        /// 指定した検出方法のデフォルトパラメータを作成します
        /// </summary>
        /// <param name="method">検出方法</param>
        /// <returns>デフォルトパラメータ</returns>
        public static TextDetectionParams CreateForMethod(TextDetectionMethod method)
        {
            var parameters = new TextDetectionParams { Method = method };
            
            // 初期化は既存のプロパティに依存しているため、最初に共通パラメータを定義
            parameters.Parameters["MinWidth"] = 10;
            parameters.Parameters["MinHeight"] = 10;
            parameters.Parameters["MinAspectRatio"] = 0.1f;
            parameters.Parameters["MaxAspectRatio"] = 10.0f;
            parameters.Parameters["MergeThreshold"] = 0.5f;
            
            switch (method)
            {
                case TextDetectionMethod.Mser:
                    parameters.Parameters["delta"] = 5;
                    parameters.Parameters["minArea"] = 60;
                    parameters.Parameters["maxArea"] = 14400;
                    parameters.Parameters["maxVariation"] = 0.25;
                    parameters.Parameters["minDiversity"] = 0.2;
                    break;
                    
                case TextDetectionMethod.ConnectedComponents:
                    parameters.Parameters["minSize"] = 10;
                    parameters.Parameters["maxSize"] = 10000;
                    parameters.Parameters["connectivity"] = 8;
                    break;
                    
                case TextDetectionMethod.Contours:
                    parameters.Parameters["mode"] = 1; // RETR_EXTERNAL
                    parameters.Parameters["method"] = 2; // CHAIN_APPROX_SIMPLE
                    parameters.Parameters["minArea"] = 50;
                    parameters.Parameters["maxArea"] = 10000;
                    break;
                    
                case TextDetectionMethod.EdgeBased:
                    parameters.Parameters["threshold1"] = 100;
                    parameters.Parameters["threshold2"] = 200;
                    parameters.Parameters["apertureSize"] = 3;
                    break;
                    
                case TextDetectionMethod.Swt:
                    parameters.Parameters["darkTextOnLight"] = true;
                    parameters.Parameters["strokeWidthRatio"] = 3.0f;
                    parameters.Parameters["minStrokeWidth"] = 2.0f;
                    parameters.Parameters["maxStrokeWidth"] = 20.0f;
                    break;
                    
                case TextDetectionMethod.Combined:
                    // 複合メソッドのデフォルトはMSERと連結成分の組み合わせ
                    parameters.Parameters["methods"] = new[] { TextDetectionMethod.Mser, TextDetectionMethod.ConnectedComponents };
                    parameters.Parameters["weights"] = new[] { 0.6, 0.4 };
                    break;
            }
            
            return parameters;
        }
        
        /// <summary>
        /// ディクショナリから値を取得し、存在しない場合はデフォルト値を返します
        /// </summary>
        private T GetOrDefault<T>(string key, T defaultValue)
        {
            if (Parameters.TryGetValue(key, out var value))
            {
                try
                {
                    return (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
                }
                catch (InvalidCastException)
                {
                    return defaultValue;
                }
                catch (FormatException)
                {
                    return defaultValue;
                }
                catch (OverflowException)
                {
                    return defaultValue;
                }
            }
            
            return defaultValue;
        }
    }
