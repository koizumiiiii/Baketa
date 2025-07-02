using System;
using Baketa.Core.Abstractions.Imaging;

namespace Baketa.Core.Abstractions.Imaging.Pipeline;

/// <summary>
/// パイプライン処理における画像情報を表すクラス
/// 名前空間の衝突を避けるため、ImageInfo から PipelineImageInfo にリネーム
/// </summary>
/// <remarks>
/// コンストラクタ
/// </remarks>
public class PipelineImageInfo(int width, int height, int channels, ImageFormat format, PipelineStage stage)
{
    /// <summary>
    /// 画像の幅
    /// </summary>
    public int Width { get; } = width;

    /// <summary>
    /// 画像の高さ
    /// </summary>
    public int Height { get; } = height;

    /// <summary>
    /// 画像のチャンネル数
    /// </summary>
    public int Channels { get; } = channels;

    /// <summary>
    /// 画像のフォーマット
    /// </summary>
    public ImageFormat Format { get; } = format;

    /// <summary>
    /// パイプラインの処理段階
    /// </summary>
    public PipelineStage Stage { get; } = stage;

    /// <summary>
    /// 標準的な画像情報からパイプライン画像情報を作成
    /// </summary>
    public static PipelineImageInfo FromImageInfo(ImageInfo imageInfo, PipelineStage stage)
        {
            ArgumentNullException.ThrowIfNull(imageInfo);

            return new PipelineImageInfo(
                imageInfo.Width,
                imageInfo.Height,
                imageInfo.Channels,
                imageInfo.Format,
                stage
            );
        }

        /// <summary>
        /// 画像から直接パイプライン画像情報を作成
        /// </summary>
        public static PipelineImageInfo FromImage(IAdvancedImage image, PipelineStage stage)
        {
            ArgumentNullException.ThrowIfNull(image);

            var imageInfo = ImageInfo.FromImage(image);
            return FromImageInfo(imageInfo, stage);
        }

        /// <summary>
        /// 標準的な画像情報に変換
        /// </summary>
        public ImageInfo ToImageInfo()
        {
            return new ImageInfo(Width, Height, Format, Channels);
        }
    }

    /// <summary>
    /// パイプライン処理の段階を表す列挙型
    /// </summary>
    public enum PipelineStage
    {
        /// <summary>
        /// 入力段階（未処理）
        /// </summary>
        Input,

        /// <summary>
        /// 前処理段階
        /// </summary>
        Preprocessing,

        /// <summary>
        /// 処理段階
        /// </summary>
        Processing,

        /// <summary>
        /// 後処理段階
        /// </summary>
        Postprocessing,

        /// <summary>
        /// 出力段階（処理済み）
        /// </summary>
        Output
    }
