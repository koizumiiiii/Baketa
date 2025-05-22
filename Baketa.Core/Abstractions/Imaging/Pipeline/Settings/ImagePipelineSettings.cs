using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Baketa.Core.Abstractions.Imaging.Pipeline.Settings;

    /// <summary>
    /// パイプライン設定を表すクラス
    /// </summary>
    public class ImagePipelineSettings
    {
        /// <summary>
        /// パイプライン名
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
        
        /// <summary>
        /// パイプライン説明
        /// </summary>
        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;
        
        /// <summary>
        /// フィルター設定リスト
        /// </summary>
        [JsonPropertyName("filters")]
        public IReadOnlyList<FilterSettings> Filters => _filters;
        private readonly List<FilterSettings> _filters = [];
        
        /// <summary>
        /// フィルター設定を追加します
        /// </summary>
        /// <param name="filter">追加するフィルター設定</param>
        public void AddFilter(FilterSettings filter)
        {
            _filters.Add(filter);
        }
        
        /// <summary>
        /// フィルター設定をクリアします
        /// </summary>
        public void ClearFilters()
        {
            _filters.Clear();
        }
        
        /// <summary>
        /// 中間結果の保存モード
        /// </summary>
        [JsonPropertyName("intermediateResultMode")]
        public IntermediateResultMode IntermediateResultMode { get; set; } = IntermediateResultMode.None;
        
        /// <summary>
        /// グローバルエラーハンドリング戦略
        /// </summary>
        [JsonPropertyName("errorHandlingStrategy")]
        public StepErrorHandlingStrategy ErrorHandlingStrategy { get; set; } = StepErrorHandlingStrategy.StopExecution;
        
        /// <summary>
        /// 追加のメタデータ
        /// </summary>
        [JsonPropertyName("metadata")]
        public Dictionary<string, string> Metadata { get; private set; } = [];
        
        /// <summary>
        /// デフォルトコンストラクタ
        /// </summary>
        public ImagePipelineSettings()
        {
        }
        
        /// <summary>
        /// 名前と説明を指定して初期化
        /// </summary>
        /// <param name="name">パイプライン名</param>
        /// <param name="description">パイプライン説明</param>
        public ImagePipelineSettings(string name, string description)
        {
            Name = name;
            Description = description;
        }
    }
