using System;
using System.Collections.Generic;
using Baketa.Core.Abstractions.Imaging;

namespace Baketa.Infrastructure.Imaging.Extensions;

    /// <summary>
    /// 画像操作に関する拡張メソッド
    /// </summary>
    public static class ImageExtensions
    {
        // メタデータを保持するための一時的なストレージ（実際の実装では画像インスタンスに保持するべき）
        private static readonly Dictionary<IAdvancedImage, Dictionary<string, object>> _metadataStorage = [];
            
        /// <summary>
        /// 画像にメタデータを設定します
        /// </summary>
        /// <param name="image">設定対象の画像</param>
        /// <param name="key">メタデータのキー</param>
        /// <param name="value">メタデータの値</param>
        /// <returns>メタデータを設定した画像（チェーン用）</returns>
        public static IAdvancedImage SetMetadata(this IAdvancedImage image, string key, object value)
        {
            ArgumentNullException.ThrowIfNull(image);
                
            if (string.IsNullOrEmpty(key))
                ArgumentException.ThrowIfNullOrEmpty(key, $"キーが null または空です");
                
            // ストレージにメタデータを追加
            if (!_metadataStorage.TryGetValue(image, out var metadata))
            {
                metadata = [];
                _metadataStorage[image] = metadata;
            }
            
            metadata[key] = value;
            
            return image;
        }
        
        /// <summary>
        /// 画像からメタデータを取得します
        /// </summary>
        /// <param name="image">取得対象の画像</param>
        /// <param name="key">メタデータのキー</param>
        /// <param name="value">取得したメタデータ値</param>
        /// <returns>メタデータが存在した場合はtrue、そうでない場合はfalse</returns>
        public static bool TryGetMetadata(this IAdvancedImage image, string key, out object value)
        {
            ArgumentNullException.ThrowIfNull(image);
                
            if (string.IsNullOrEmpty(key))
                ArgumentException.ThrowIfNullOrEmpty(key, $"キーが null または空です");
                
            // デフォルト値で初期化してnull求値式を避ける
            value = string.Empty; // 初期化値を明確に設定
            
            // ストレージからメタデータを取得
            if (_metadataStorage.TryGetValue(image, out var metadata) && 
                metadata.TryGetValue(key, out var foundValue))
            {
                value = foundValue; // nullの場合でも安全に代入
                return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// 画像からメタデータを取得します
        /// </summary>
        /// <typeparam name="T">メタデータの型</typeparam>
        /// <param name="image">取得対象の画像</param>
        /// <param name="key">メタデータのキー</param>
        /// <param name="value">取得したメタデータ値</param>
        /// <returns>メタデータが存在した場合はtrue、そうでない場合はfalse</returns>
        public static bool TryGetMetadata<T>(this IAdvancedImage image, string key, out T? value) where T : class
        {
            ArgumentNullException.ThrowIfNull(image);
            
            value = null;
            
            if (TryGetMetadata(image, key, out var objValue) && objValue is T typedValue)
            {
                value = typedValue;
                return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// 画像のメタデータを削除します
        /// </summary>
        /// <param name="image">削除対象の画像</param>
        /// <param name="key">メタデータのキー</param>
        /// <returns>メタデータが削除された場合はtrue、そうでない場合はfalse</returns>
        public static bool RemoveMetadata(this IAdvancedImage image, string key)
        {
            ArgumentNullException.ThrowIfNull(image);
                
            if (string.IsNullOrEmpty(key))
                ArgumentException.ThrowIfNullOrEmpty(key, $"キーが null または空です");
                
            // ストレージからメタデータを削除
            if (_metadataStorage.TryGetValue(image, out var metadata))
            {
                return metadata.Remove(key);
            }
            
            return false;
        }
        
        /// <summary>
        /// 画像のすべてのメタデータをクリアします
        /// </summary>
        /// <param name="image">クリア対象の画像</param>
        public static void ClearMetadata(this IAdvancedImage image)
        {
            ArgumentNullException.ThrowIfNull(image);
                
            _metadataStorage.Remove(image);
        }
    }
