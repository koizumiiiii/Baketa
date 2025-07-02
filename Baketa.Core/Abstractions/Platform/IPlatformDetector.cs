using System;
using System.Threading.Tasks;

namespace Baketa.Core.Abstractions.Platform;

    /// <summary>
    /// プラットフォーム検出インターフェース
    /// </summary>
    public interface IPlatformDetector
    {
        /// <summary>
        /// 現在のプラットフォームを検出
        /// </summary>
        /// <returns>プラットフォーム情報</returns>
        IPlatform DetectCurrentPlatform();
        
        /// <summary>
        /// 非同期で現在のプラットフォームを検出
        /// </summary>
        /// <returns>プラットフォーム情報</returns>
        Task<IPlatform> DetectCurrentPlatformAsync();
        
        /// <summary>
        /// Windows環境かどうかを確認
        /// </summary>
        /// <returns>Windows環境の場合はtrue</returns>
        bool IsWindows();
        
        /// <summary>
        /// 特定のプラットフォーム機能がサポートされているか確認
        /// </summary>
        /// <param name="featureName">機能名</param>
        /// <returns>サポートされている場合はtrue</returns>
        bool IsPlatformFeatureSupported(string featureName);
    }
