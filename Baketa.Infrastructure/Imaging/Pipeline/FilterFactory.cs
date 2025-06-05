using System;
using System.Collections.Generic;
using System.Linq;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Imaging.Pipeline;
using Baketa.Core.Abstractions.OCR.TextDetection;
using Baketa.Infrastructure.Imaging.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Imaging.Pipeline;

    /// <summary>
    /// フィルターファクトリー実装
    /// </summary>
    public class FilterFactory : IFilterFactory
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<FilterFactory>? _logger;
        private readonly Dictionary<string, Type> _filterTypes = [];
        
        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="serviceProvider">サービスプロバイダー</param>
        /// <param name="logger">ロガー</param>
        public FilterFactory(IServiceProvider serviceProvider, ILogger<FilterFactory>? logger = null)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _logger = logger;
            
            // 利用可能なフィルタータイプを登録
            RegisterFilterTypes();
        }
        
        /// <summary>
        /// フィルタータイプを登録します
        /// </summary>
        private void RegisterFilterTypes()
        {
            // テキスト検出フィルター
            _filterTypes["TextRegionDetectionFilter"] = typeof(TextRegionDetectionFilter);
            
            // 他のフィルターはここに追加
            // TODO: フィルターの自動検出または明示的な登録
        }
        
        /// <summary>
        /// タイプ名からフィルターを生成します
        /// </summary>
        /// <param name="typeName">フィルタータイプ名</param>
        /// <returns>生成されたフィルター、または生成できない場合はnull</returns>
        public IImageFilter? CreateFilter(string typeName)
        {
            return CreateFilter(typeName, new Dictionary<string, object>());
        }
        
        /// <summary>
        /// タイプ名とパラメータからフィルターを生成します
        /// </summary>
        /// <param name="typeName">フィルタータイプ名</param>
        /// <param name="parameters">フィルターパラメータ</param>
        /// <returns>生成されたフィルター、または生成できない場合はnull</returns>
        public IImageFilter? CreateFilter(string typeName, IDictionary<string, object> parameters)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                throw new ArgumentException("フィルタータイプ名が指定されていません", nameof(typeName));
            }
            
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters), "パラメーターディクショナリがnullです");
            }
            
            try
            {
                if (_filterTypes.TryGetValue(typeName, out var filterType))
                {
                    // DI経由でフィルターを生成
                    if (_serviceProvider.GetService(filterType) is IImageFilter filter)
                    {
                        // パラメータを設定
                        foreach (var param in parameters)
                        {
                            filter.SetParameter(param.Key, param.Value);
                        }
                        
                        return filter;
                    }
                }
                
                // 完全修飾名による検索
                if (Type.GetType(typeName) is { } t && typeof(IImageFilter).IsAssignableFrom(t))
                {
                    // Activatorを使用してインスタンス化
                    if (ActivatorUtilities.CreateInstance(_serviceProvider, t) is IImageFilter filter)
                    {
                        // パラメータを設定
                        foreach (var param in parameters)
                        {
                            filter.SetParameter(param.Key, param.Value);
                        }
                        
                        return filter;
                    }
                }
                
                _logger?.LogWarning("フィルタータイプ '{FilterType}' が見つかりません", typeName);
                return null;
            }
            catch (InvalidOperationException ex)
            {
                _logger?.LogError(ex, "フィルター '{FilterType}' の生成中に操作エラーが発生しました", typeName);
                return null;
            }
            catch (ArgumentException ex)
            {
                _logger?.LogError(ex, "フィルター '{FilterType}' の生成中に引数エラーが発生しました", typeName);
                return null;
            }
            catch (MissingMethodException ex)
            {
                _logger?.LogError(ex, "フィルター '{FilterType}' の生成中にコンストラクターが見つかりませんでした", typeName);
                return null;
            }
            catch (TypeLoadException ex)
            {
                _logger?.LogError(ex, "フィルター '{FilterType}' の型ロード中にエラーが発生しました", typeName);
                return null;
            }
        }
        
        /// <summary>
        /// 利用可能なすべてのフィルタータイプを取得します
        /// </summary>
        /// <returns>フィルタータイプ名のリスト</returns>
        public IEnumerable<string> GetAvailableFilterTypes()
        {
            return [.. _filterTypes.Keys];
        }
        
        /// <summary>
        /// 指定されたカテゴリのフィルタータイプを取得します
        /// </summary>
        /// <param name="category">フィルターカテゴリ</param>
        /// <returns>フィルタータイプ名のリスト</returns>
        public IEnumerable<string> GetFilterTypesByCategory(FilterCategory category)
        {
            // 現在の実装では対応していないため、空リストを返す
            _logger?.LogWarning("カテゴリによるフィルター検索は現在実装されていません");
            // IDE0300/CA1825警告に対応
            return [];
            // return Array.Empty<string>();
        }
    }
