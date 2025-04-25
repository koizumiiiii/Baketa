using System;
using Baketa.Core.Abstractions.OCR;
using OpenCvSharp;

namespace Baketa.Infrastructure.Platform.Windows.OpenCv.Extensions
{
    /// <summary>
    /// OpenCV関連の拡張メソッドを提供するクラス
    /// </summary>
    internal static class OpenCvExtensions
    {
        /// <summary>
        /// ThresholdTypeをOpenCVのThresholdTypesに変換します
        /// </summary>
        /// <param name="type">変換するThresholdType</param>
        /// <returns>対応するOpenCVのThresholdTypes</returns>
        public static ThresholdTypes ConvertThresholdType(ThresholdType type)
        {
            return type switch
            {
                ThresholdType.Binary => ThresholdTypes.Binary,
                ThresholdType.BinaryInv => ThresholdTypes.BinaryInv,
                ThresholdType.Truncate => ThresholdTypes.Trunc,
                ThresholdType.ToZero => ThresholdTypes.Tozero,
                ThresholdType.ToZeroInv => ThresholdTypes.TozeroInv,
                ThresholdType.Otsu => ThresholdTypes.Binary | ThresholdTypes.Otsu,
                ThresholdType.Adaptive => throw new ArgumentException("適応的閾値処理にはApplyAdaptiveThresholdAsyncメソッドを使用してください", nameof(type)),
                _ => throw new ArgumentException($"未サポートの閾値処理タイプ: {type}", nameof(type))
            };
        }

        /// <summary>
        /// ThresholdTypeをOpenCVのバイナリThresholdTypesに変換します（適応的閾値処理用）
        /// </summary>
        /// <param name="type">変換するThresholdType</param>
        /// <returns>対応するOpenCVのThresholdTypes（Binary/BinaryInvのみ）</returns>
        public static ThresholdTypes ConvertBinaryThresholdType(ThresholdType type)
        {
            return type switch
            {
                ThresholdType.Binary => ThresholdTypes.Binary,
                ThresholdType.BinaryInv => ThresholdTypes.BinaryInv,
                _ => ThresholdTypes.Binary // デフォルトはBinary
            };
        }

        /// <summary>
        /// AdaptiveThresholdTypeをOpenCVのAdaptiveThresholdTypesに変換します
        /// </summary>
        /// <param name="type">変換するAdaptiveThresholdType</param>
        /// <returns>対応するOpenCVのAdaptiveThresholdTypes</returns>
        public static AdaptiveThresholdTypes ConvertAdaptiveThresholdType(AdaptiveThresholdType type)
        {
            return type switch
            {
                AdaptiveThresholdType.Mean => AdaptiveThresholdTypes.MeanC,
                AdaptiveThresholdType.Gaussian => AdaptiveThresholdTypes.GaussianC,
                _ => throw new ArgumentException($"未サポートの適応的閾値処理タイプ: {type}", nameof(type))
            };
        }

        /// <summary>
        /// MorphTypeをOpenCVのMorphTypesに変換します
        /// </summary>
        /// <param name="type">変換するMorphType</param>
        /// <returns>対応するOpenCVのMorphTypes</returns>
        public static MorphTypes ConvertMorphType(MorphType type)
        {
            return type switch
            {
                MorphType.Erode => MorphTypes.Erode,
                MorphType.Dilate => MorphTypes.Dilate,
                MorphType.Open => MorphTypes.Open,
                MorphType.Close => MorphTypes.Close,
                MorphType.Gradient => MorphTypes.Gradient,
                MorphType.TopHat => MorphTypes.TopHat,
                MorphType.BlackHat => MorphTypes.BlackHat,
                _ => throw new ArgumentException($"未サポートのモルフォロジー演算タイプ: {type}", nameof(type))
            };
        }
    }
}
