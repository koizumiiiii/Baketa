using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Baketa.Core.Abstractions.Imaging.Pipeline.Settings;

    /// <summary>
    /// フィルター設定を表すクラス
    /// </summary>
    public class FilterSettings
    {
        /// <summary>
        /// フィルタータイプ名
        /// </summary>
        [JsonPropertyName("typeName")]
        public string TypeName { get; set; } = string.Empty;
        
        /// <summary>
        /// フィルター名
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
        
        /// <summary>
        /// フィルター説明
        /// </summary>
        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;
        
        /// <summary>
        /// フィルターカテゴリ
        /// </summary>
        [JsonPropertyName("category")]
        public FilterCategory Category { get; set; } = FilterCategory.Composite;
        
        /// <summary>
        /// フィルターパラメータ
        /// </summary>
        [JsonPropertyName("parameters")]
        public Dictionary<string, object> Parameters { get; private set; } = [];
        
        /// <summary>
        /// フィルターのエラーハンドリング戦略
        /// </summary>
        [JsonPropertyName("errorHandlingStrategy")]
        public StepErrorHandlingStrategy ErrorHandlingStrategy { get; set; } = StepErrorHandlingStrategy.SkipStep;
        
        /// <summary>
        /// デフォルトコンストラクタ
        /// </summary>
        public FilterSettings()
        {
        }
        
        /// <summary>
        /// フィルタータイプ名を指定して初期化
        /// </summary>
        /// <param name="typeName">フィルタータイプ名</param>
        public FilterSettings(string typeName)
        {
            TypeName = typeName;
        }
        
        /// <summary>
        /// フィルタータイプと名前を指定して初期化
        /// </summary>
        /// <param name="typeName">フィルタータイプ名</param>
        /// <param name="name">フィルター名</param>
        /// <param name="description">フィルター説明</param>
        public FilterSettings(string typeName, string name, string description)
        {
            TypeName = typeName;
            Name = name;
            Description = description;
        }
    }
