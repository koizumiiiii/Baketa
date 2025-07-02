namespace Baketa.Core.Abstractions.Imaging;

    /// <summary>
    /// 画像情報を表すクラス
    /// </summary>
    public class ImageInfo
    {
        /// <summary>
        /// 画像の幅
        /// </summary>
        public int Width { get; set; }
        
        /// <summary>
        /// 画像の高さ
        /// </summary>
        public int Height { get; set; }
        
        /// <summary>
        /// 画像のフォーマット
        /// </summary>
        public ImageFormat Format { get; set; }
        
        /// <summary>
        /// 色チャンネル数
        /// </summary>
        public int Channels { get; set; }

        /// <summary>
        /// 画像情報の新しいインスタンスを初期化します
        /// </summary>
        public ImageInfo()
        {
        }

        /// <summary>
        /// 画像情報の新しいインスタンスを初期化します
        /// </summary>
        /// <param name="width">画像の幅</param>
        /// <param name="height">画像の高さ</param>
        /// <param name="format">画像のフォーマット</param>
        /// <param name="channels">色チャンネル数</param>
        public ImageInfo(int width, int height, ImageFormat format, int channels)
        {
            Width = width;
            Height = height;
            Format = format;
            Channels = channels;
        }

        /// <summary>
        /// 指定したIAdvancedImageから画像情報を作成します
        /// </summary>
        /// <param name="image">画像</param>
        /// <returns>画像情報</returns>
        /// <exception cref="System.ArgumentNullException">imageがnullの場合</exception>
        public static ImageInfo FromImage(IAdvancedImage image)
        {
            ArgumentNullException.ThrowIfNull(image);
            int channels = GetChannelCount(image.Format);
            
            return new ImageInfo
            {
                Width = image.Width,
                Height = image.Height,
                Format = image.Format,
                Channels = channels
            };
        }

        /// <summary>
        /// 画像フォーマットからチャンネル数を取得します
        /// </summary>
        /// <param name="format">画像フォーマット</param>
        /// <returns>チャンネル数</returns>
        public static int GetChannelCount(ImageFormat format)
        {
            return format switch
            {
                ImageFormat.Rgb24 => 3,
                ImageFormat.Rgba32 => 4,
                ImageFormat.Grayscale8 => 1,
                _ => throw new System.ArgumentException($"未サポートのフォーマット: {format}")
            };
        }
    }
