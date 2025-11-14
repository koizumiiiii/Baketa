using System;
using System.Drawing;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Platform.Windows;
using Baketa.Core.Common;
using Baketa.Infrastructure.Platform.Windows;
using CoreImageFormat = Baketa.Core.Abstractions.Imaging.ImageFormat;
using DrawingImageFormat = System.Drawing.Imaging.ImageFormat;
using Rectangle = System.Drawing.Rectangle;

namespace Baketa.Infrastructure.Platform.Adapters;

/// <summary>
/// IWindowsImageAdapterインターフェースの標準実装クラス
/// パフォーマンスと機能を強化した画像変換機能を提供
/// </summary>
public class DefaultWindowsImageAdapter : DisposableBase, IWindowsImageAdapter
{
    // WindowsImageAdapterのインスタンスを追跡するためのフィールド
    private readonly System.Collections.Generic.HashSet<WindowsImageAdapter> _createdAdapters = [];
    // 再利用可能なメモリストリーム（メモリ割り当てを減らすため）
    private readonly ThreadLocal<MemoryStream> _recycledMemoryStream = new(() => new MemoryStream(1024 * 1024)); // 初期サイズ1MB

    /// <summary>
    /// Windowsネイティブイメージをコアイメージ(IAdvancedImage)に変換します
    /// </summary>
    /// <param name="windowsImage">変換元のWindowsイメージ</param>
    /// <returns>変換後のAdvancedImage</returns>
    public IAdvancedImage ToAdvancedImage(IWindowsImage windowsImage)
    {
        ArgumentNullException.ThrowIfNull(windowsImage, nameof(windowsImage));
        var adapter = new WindowsImageAdapter(windowsImage);
        // 作成したアダプターを追跡
        _createdAdapters.Add(adapter);
        return adapter;
    }

    /// <summary>
    /// Windowsネイティブイメージをコアイメージ(IImage)に変換します
    /// </summary>
    /// <param name="windowsImage">変換元のWindowsイメージ</param>
    /// <returns>変換後のImage</returns>
    public IImage ToImage(IWindowsImage windowsImage)
    {
        ArgumentNullException.ThrowIfNull(windowsImage, nameof(windowsImage));
        // IAdvancedImageはIImageを継承しているので、ToAdvancedImageの結果をそのまま返す
        return ToAdvancedImage(windowsImage);
    }

    /// <summary>
    /// Windows画像をコア画像に変換
    /// </summary>
    /// <param name="windowsImage">Windows画像</param>
    /// <returns>コア画像インターフェース</returns>
    public IImage AdaptToImage(IWindowsImage windowsImage)
    {
        return ToImage(windowsImage);
    }

    /// <summary>
    /// コア画像をWindows画像に変換（可能な場合）
    /// </summary>
    /// <param name="image">コア画像</param>
    /// <returns>Windows画像、変換できない場合はnull</returns>
    public IWindowsImage AdaptToWindowsImage(IImage image)
    {
        if (image is WindowsImageAdapter adapter)
        {
            return GetWindowsImage(adapter);
        }

        throw new NotSupportedException("この画像タイプからWindowsImageへの変換はサポートされていません");
    }

    /// <summary>
    /// 非同期でWindows画像をコア画像に変換
    /// </summary>
    /// <param name="windowsImage">Windows画像</param>
    /// <returns>コア画像インターフェース</returns>
    public Task<IImage> AdaptToImageAsync(IWindowsImage windowsImage)
    {
        return Task.FromResult(AdaptToImage(windowsImage));
    }

    /// <summary>
    /// 非同期でコア画像をWindows画像に変換（可能な場合）
    /// </summary>
    /// <param name="image">コア画像</param>
    /// <returns>Windows画像</returns>
    public Task<IWindowsImage> AdaptToWindowsImageAsync(IImage image)
    {
        return Task.FromResult(AdaptToWindowsImage(image));
    }

    /// <summary>
    /// アダプターがサポートする機能名
    /// </summary>
    public string FeatureName => "Windows画像変換アダプター";

    /// <summary>
    /// 特定の型変換をサポートするかどうか
    /// </summary>
    /// <typeparam name="TSource">ソース型</typeparam>
    /// <typeparam name="TTarget">ターゲット型</typeparam>
    /// <returns>サポートする場合はtrue</returns>
    public bool SupportsConversion<TSource, TTarget>()
    {
        return (typeof(TSource) == typeof(IWindowsImage) && typeof(TTarget) == typeof(IImage)) ||
               (typeof(TSource) == typeof(IImage) && typeof(TTarget) == typeof(IWindowsImage));
    }

    /// <summary>
    /// 変換を試行
    /// </summary>
    /// <typeparam name="TSource">ソース型</typeparam>
    /// <typeparam name="TTarget">ターゲット型</typeparam>
    /// <param name="source">ソースオブジェクト</param>
    /// <param name="target">変換結果（出力）</param>
    /// <returns>変換成功時はtrue</returns>
    public bool TryConvert<TSource, TTarget>(TSource source, out TTarget target) where TSource : class where TTarget : class
    {
        target = null!;

        try
        {
            if (source is IWindowsImage windowsImage && typeof(TTarget) == typeof(IImage))
            {
                target = (TTarget)(object)AdaptToImage(windowsImage);
                return true;
            }

            if (source is IImage image && typeof(TTarget) == typeof(IWindowsImage))
            {
                target = (TTarget)(object)AdaptToWindowsImage(image);
                return true;
            }
        }
        catch
        {
            // 変換失敗時はfalseを返す
        }

        return false;
    }

    /// <summary>
    /// コアイメージ(IAdvancedImage)をWindowsネイティブイメージに変換します
    /// 最適化された変換処理を使用します
    /// </summary>
    /// <param name="advancedImage">変換元のAdvancedImage</param>
    /// <returns>変換後のWindowsイメージ</returns>
    public async Task<IWindowsImage> FromAdvancedImageAsync(IAdvancedImage advancedImage)
    {
        ArgumentNullException.ThrowIfNull(advancedImage, nameof(advancedImage));

        // WindowsImageAdapterからの変換を最適化
        if (advancedImage is WindowsImageAdapter windowsAdapter)
        {
            // 既にWindowsImageを持っている場合は、それをクローンして返す
            var nativeImage = GetWindowsImage(windowsAdapter).GetNativeImage();
            if (nativeImage is Bitmap originalBitmap)
            {
                var clonedBitmap = (Bitmap)originalBitmap.Clone();
                return new WindowsImage(clonedBitmap);
            }
        }

        // バイト配列を経由して変換する場合は、再利用可能なストリームを使用
        var imageBytes = await advancedImage.ToByteArrayAsync().ConfigureAwait(false);
        var stream = _recycledMemoryStream.Value!;
        try
        {
            stream.Position = 0;
            stream.SetLength(0);
            await stream.WriteAsync(imageBytes.AsMemory()).ConfigureAwait(false);
            stream.Position = 0;

            using var bitmap = new Bitmap(stream);
            using var persistentBitmap = (Bitmap)bitmap.Clone();
            return new WindowsImage(persistentBitmap);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("画像の変換に失敗しました", ex);
        }
    }

    /// <summary>
    /// コアイメージ(IImage)をWindowsネイティブイメージに変換します
    /// 最適化された変換処理を使用します
    /// </summary>
    /// <param name="image">変換元のImage</param>
    /// <returns>変換後のWindowsイメージ</returns>
    public async Task<IWindowsImage> FromImageAsync(IImage image)
    {
        ArgumentNullException.ThrowIfNull(image, nameof(image));

        // IAdvancedImageの場合は特化したメソッドを使用
        if (image is IAdvancedImage advancedImage)
        {
            return await FromAdvancedImageAsync(advancedImage).ConfigureAwait(false);
        }

        // バイト配列を経由して変換する場合は、再利用可能なストリームを使用
        var imageBytes = await image.ToByteArrayAsync().ConfigureAwait(false);
        var stream = _recycledMemoryStream.Value!;
        try
        {
            stream.Position = 0;
            stream.SetLength(0);
            await stream.WriteAsync(imageBytes.AsMemory()).ConfigureAwait(false);
            stream.Position = 0;

            using var bitmap = new Bitmap(stream);
            var persistentBitmap = (Bitmap)bitmap.Clone();
            return new WindowsImage(persistentBitmap);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("画像の変換に失敗しました", ex);
        }
    }

    /// <summary>
    /// Bitmapからコアイメージ(IAdvancedImage)を作成します
    /// </summary>
    /// <param name="bitmap">変換元のBitmap</param>
    /// <returns>変換後のAdvancedImage</returns>
    public IAdvancedImage CreateAdvancedImageFromBitmap(Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap, nameof(bitmap));

        // BitmapをWindowsImageに変換し、それをAdvancedImageに変換
        var windowsImage = new WindowsImage((Bitmap)bitmap.Clone());
        return ToAdvancedImage(windowsImage);
    }

    /// <summary>
    /// バイト配列からコアイメージ(IAdvancedImage)を作成します
    /// 最適化された変換処理を使用します
    /// </summary>
    /// <param name="imageData">画像データのバイト配列</param>
    /// <returns>変換後のAdvancedImage</returns>
    public async Task<IAdvancedImage> CreateAdvancedImageFromBytesAsync(byte[] imageData)
    {
        ArgumentNullException.ThrowIfNull(imageData, nameof(imageData));

        try
        {
            // 再利用可能なストリームを使用
            var stream = _recycledMemoryStream.Value!;
            stream.Position = 0;
            stream.SetLength(0);
            await stream.WriteAsync(imageData.AsMemory()).ConfigureAwait(false);
            stream.Position = 0;

            // 画像フォーマットを検出（ヘッダー解析）
            var imageFormat = DetectImageFormat(imageData);

            // 画像作成前にストリームの有効性をチェック
            if (stream.Length == 0 || stream.Length < 8)
            {
                throw new ArgumentException("無効な画像データです", nameof(imageData));
            }

            try
            {
                // 通常サイズの画像は標準的な処理
                using var bitmap = new Bitmap(stream);
                // Bitmapの有効性を確認
                if (bitmap.Width <= 0 || bitmap.Height <= 0)
                {
                    throw new ArgumentException("無効なBitmapが生成されました");
                }

                // 所有権移転のためのクローン作成
                using var persistentBitmap = (Bitmap)bitmap.Clone();
                var windowsImage = new WindowsImage(persistentBitmap);
                var result = ToAdvancedImage(windowsImage);
                return result;
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException("画像データからBitmapの作成に失敗しました", ex);
            }
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException("無効な画像データです", nameof(imageData), ex);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("画像の作成に失敗しました", ex);
        }
    }

    /// <summary>
    /// ファイルからコアイメージ(IAdvancedImage)を作成します
    /// 効率的なストリーム処理を使用して、大きな画像ファイルも扱えます
    /// </summary>
    /// <param name="filePath">画像ファイルのパス</param>
    /// <returns>変換後のAdvancedImage</returns>
    public async Task<IAdvancedImage> CreateAdvancedImageFromFileAsync(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath, nameof(filePath));

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("指定されたファイルが見つかりません", filePath);
        }

        try
        {
            // 非常に大きなファイルの場合はストリーム処理を使用
            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length > 50 * 1024 * 1024) // 50MB以上
            {
                // 大きなファイルはストリーム処理
                using var bitmap = new Bitmap(filePath);
                // 所有権移転のためのクローン作成
                using var persistentBitmap = (Bitmap)bitmap.Clone();
                var windowsImage = new WindowsImage(persistentBitmap);
                return ToAdvancedImage(windowsImage);
            }
            else
            {
                // 通常サイズのファイルはバイト配列経由
                var imageData = await File.ReadAllBytesAsync(filePath).ConfigureAwait(false);
                return await CreateAdvancedImageFromBytesAsync(imageData).ConfigureAwait(false);
            }
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException($"ファイル '{filePath}' は有効な画像ではありません", nameof(filePath), ex);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"ファイル '{filePath}' から画像を作成できませんでした", ex);
        }
    }

    /// <summary>
    /// バイト配列から画像フォーマットを検出
    /// </summary>
    /// <param name="imageData">画像データのバイト配列</param>
    /// <returns>検出された画像フォーマット</returns>
    private static DrawingImageFormat DetectImageFormat(byte[] imageData)
    {
        // ヘッダーの最初の数バイトでフォーマットを判定
        if (imageData.Length < 8)
        {
            return DrawingImageFormat.Png; // デフォルト
        }

        // JPEG: FF D8 FF
        if (imageData[0] == 0xFF && imageData[1] == 0xD8 && imageData[2] == 0xFF)
        {
            return DrawingImageFormat.Jpeg;
        }
        // PNG: 89 50 4E 47 0D 0A 1A 0A
        else if (imageData[0] == 0x89 && imageData[1] == 0x50 && imageData[2] == 0x4E && imageData[3] == 0x47
                && imageData[4] == 0x0D && imageData[5] == 0x0A && imageData[6] == 0x1A && imageData[7] == 0x0A)
        {
            return DrawingImageFormat.Png;
        }
        // BMP: 42 4D
        else if (imageData[0] == 0x42 && imageData[1] == 0x4D)
        {
            return DrawingImageFormat.Bmp;
        }
        // GIF: 47 49 46 38
        else if (imageData[0] == 0x47 && imageData[1] == 0x49 && imageData[2] == 0x46 && imageData[3] == 0x38)
        {
            return DrawingImageFormat.Gif;
        }
        // デフォルト
        return DrawingImageFormat.Png;
    }

    /// <summary>
    /// ストリームの内容が大きな画像かどうかを判定
    /// </summary>
    /// <param name="stream">チェックするストリーム</param>
    /// <returns>大きな画像の場合はtrue</returns>
    private static bool IsLargeImage(Stream stream)
    {
        long currentPosition = stream.Position;
        try
        {
            // 10MB以上のストリームは大きな画像と判断
            return stream.Length > 10 * 1024 * 1024;
        }
        finally
        {
            // ストリーム位置を元に戻す
            stream.Position = currentPosition;
        }
    }

    /// <summary>
    /// 大きな画像を最適化して読み込む
    /// </summary>
    /// <param name="stream">画像データのストリーム</param>
    /// <param name="format">画像フォーマット</param>
    /// <returns>最適化されたBitmap</returns>
    private static Bitmap OptimizedBitmapLoad(Stream stream, DrawingImageFormat format)
    {
        // 最適化オプションの設定
        // 一時的なBitmapを生成してオプションを取得
        using (var tempBitmap = new Bitmap(1, 1))
        {
            // PropertyItemsを使用する場合はここで処理
        }

        // 実際の読み込み
        var bitmap = new Bitmap(stream);

        // フォーマット固有の最適化
        if (format == DrawingImageFormat.Jpeg)
        {
            // JPEGの場合は特別な処理（必要であれば）
        }

        return bitmap;
    }

    /// <summary>
    /// WindowsImageAdapterからWindowsImageを取得するための拡張メソッド
    /// </summary>
    private static IWindowsImage GetWindowsImage(WindowsImageAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter, nameof(adapter));

        // リフレクションを使用して非公開フィールドにアクセス
        // 注：本番コードではこのような方法は通常避けるべきですが、性能最適化のために使用
        var field = typeof(WindowsImageAdapter).GetField("_windowsImage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (field != null)
        {
            return (IWindowsImage)field.GetValue(adapter)!;
        }
        throw new InvalidOperationException("WindowsImageAdapterから内部のWindowsImageを取得できません");
    }

    /// <summary>
    /// CoreフォーマットからDrawing.Imagingフォーマットへの変換
    /// </summary>
    private static DrawingImageFormat ConvertToDrawingFormat(CoreImageFormat format)
    {
        return format switch
        {
            CoreImageFormat.Png => DrawingImageFormat.Png,
            CoreImageFormat.Jpeg => DrawingImageFormat.Jpeg,
            CoreImageFormat.Bmp => DrawingImageFormat.Bmp,
            _ => DrawingImageFormat.Png // デフォルト
        };
    }

    /// <summary>
    /// Drawing.Imagingフォーマットからコアフォーマットへの変換
    /// </summary>
    private static CoreImageFormat ConvertToCoreFormat(DrawingImageFormat format)
    {
        if (format == DrawingImageFormat.Png) return CoreImageFormat.Png;
        if (format == DrawingImageFormat.Jpeg) return CoreImageFormat.Jpeg;
        if (format == DrawingImageFormat.Bmp) return CoreImageFormat.Bmp;
        return CoreImageFormat.Unknown;
    }

    /// <summary>
    /// Disposeパターンの実装 - アンマネージリソースの開放
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // CA2213対応：ThreadLocal<MemoryStream>の明示的なDisposeを行う
            try
            {
                if (_recycledMemoryStream?.IsValueCreated == true && _recycledMemoryStream.Value != null)
                {
                    _recycledMemoryStream.Value.Dispose();
                }
                _recycledMemoryStream?.Dispose();
            }
            catch (ObjectDisposedException)
            {
                // 既に破棄済みの場合は何もしない
            }

            // CA2213対応：作成した全てのWindowsImageAdapterを破棄
            try
            {
                if (_createdAdapters != null)
                {
                    foreach (var adapter in _createdAdapters.ToArray())
                    {
                        adapter?.Dispose();
                    }
                    _createdAdapters.Clear();
                }
            }
            catch (ObjectDisposedException)
            {
                // 既に破棄済みの場合は何もしない
            }
        }

        base.Dispose(disposing);
    }
}
