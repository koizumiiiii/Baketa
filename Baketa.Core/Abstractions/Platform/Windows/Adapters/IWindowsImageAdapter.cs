using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;

namespace Baketa.Core.Abstractions.Platform.Windows.Adapters
{
    /// <summary>
    /// Windows画像をコア画像に変換するアダプターインターフェース
    /// </summary>
    public interface IWindowsImageAdapter : IWindowsAdapter
    {
        /// <summary>
        /// Windows画像をコア画像に変換
        /// </summary>
        /// <param name="windowsImage">Windows画像</param>
        /// <returns>コア画像インターフェース</returns>
        IImage AdaptToImage(IWindowsImage windowsImage);
        
        /// <summary>
        /// コア画像をWindows画像に変換（可能な場合）
        /// </summary>
        /// <param name="image">コア画像</param>
        /// <returns>Windows画像、変換できない場合はnull</returns>
        IWindowsImage AdaptToWindowsImage(IImage image);
        
        /// <summary>
        /// 非同期でWindows画像をコア画像に変換
        /// </summary>
        /// <param name="windowsImage">Windows画像</param>
        /// <returns>コア画像インターフェース</returns>
        Task<IImage> AdaptToImageAsync(IWindowsImage windowsImage);
        
        /// <summary>
        /// 非同期でコア画像をWindows画像に変換（可能な場合）
        /// </summary>
        /// <param name="image">コア画像</param>
        /// <returns>Windows画像</returns>
        Task<IWindowsImage> AdaptToWindowsImageAsync(IImage image);
    }
}