namespace Baketa.Core.Abstractions.Imaging;

    /// <summary>
    /// 画像フォーマット
    /// </summary>
    public enum ImageFormat
    {
        /// <summary>
        /// 不明なフォーマット
        /// </summary>
        Unknown = 0,
        
        /// <summary>
        /// RGB24フォーマット
        /// </summary>
        Rgb24 = 1,
        
        /// <summary>
        /// RGBA32フォーマット
        /// </summary>
        Rgba32 = 2,
        
        /// <summary>
        /// グレースケール
        /// </summary>
        Grayscale8 = 3,
        
        /// <summary>
        /// PNG
        /// </summary>
        Png = 10,
        
        /// <summary>
        /// JPEG
        /// </summary>
        Jpeg = 11,
        
        /// <summary>
        /// BMP
        /// </summary>
        Bmp = 12
    }
