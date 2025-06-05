using System;
using Baketa.Core.Abstractions.OCR;
using OpenCvSharp;

namespace Baketa.Infrastructure.Platform.Windows.OpenCv.Extensions;

public static class OpenCvExtensions
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
                ThresholdType.Binary => OpenCvSharp.ThresholdTypes.Binary,
                ThresholdType.BinaryInv => OpenCvSharp.ThresholdTypes.BinaryInv,
                ThresholdType.Truncate => OpenCvSharp.ThresholdTypes.Trunc,
                ThresholdType.ToZero => OpenCvSharp.ThresholdTypes.Tozero,
                ThresholdType.ToZeroInv => OpenCvSharp.ThresholdTypes.TozeroInv,
                ThresholdType.Otsu => OpenCvSharp.ThresholdTypes.Binary | OpenCvSharp.ThresholdTypes.Otsu,
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
                ThresholdType.Binary => OpenCvSharp.ThresholdTypes.Binary,
                ThresholdType.BinaryInv => OpenCvSharp.ThresholdTypes.BinaryInv,
                _ => OpenCvSharp.ThresholdTypes.Binary // デフォルトはBinary
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
                AdaptiveThresholdType.Mean => OpenCvSharp.AdaptiveThresholdTypes.MeanC,
                AdaptiveThresholdType.Gaussian => OpenCvSharp.AdaptiveThresholdTypes.GaussianC,
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
                MorphType.Erode => OpenCvSharp.MorphTypes.Erode,
                MorphType.Dilate => OpenCvSharp.MorphTypes.Dilate,
                MorphType.Open => OpenCvSharp.MorphTypes.Open,
                MorphType.Close => OpenCvSharp.MorphTypes.Close,
                MorphType.Gradient => OpenCvSharp.MorphTypes.Gradient,
                MorphType.TopHat => OpenCvSharp.MorphTypes.TopHat,
                MorphType.BlackHat => OpenCvSharp.MorphTypes.BlackHat,
                _ => throw new ArgumentException($"未サポートのモルフォロジー演算タイプ: {type}", nameof(type))
            };
        }
    }
