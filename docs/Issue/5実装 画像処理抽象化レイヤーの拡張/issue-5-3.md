# 実装: 画像処理パイプラインの構築

## 概要
複数の画像処理フィルターを組み合わせて一連の処理を実行する画像処理パイプラインを実装します。

## 目的・理由
OCR前処理では複数の画像処理ステップを組み合わせることが一般的です。これらの処理を再利用可能なパイプラインとして構築することで、ゲームタイトルや画面状態ごとに最適な前処理を適用でき、OCR精度を向上させることができます。

## 詳細
- `IImagePipeline`インターフェースの設計と実装
- パイプライン構築機能の実装
- パイプライン設定の永続化機能
- パイプラインのプロファイル管理機能

## タスク分解
- [ ] `IImagePipeline`インターフェースの設計
  - [ ] パイプラインの基本メソッド定義
  - [ ] パイプライン構築APIの設計
- [ ] `ImagePipeline`クラスの実装
  - [ ] ビルダーパターンによる構築機能
  - [ ] パイプライン実行機能
  - [ ] パフォーマンス監視機能
- [ ] パイプライン設定の実装
  - [ ] 設定クラスの設計
  - [ ] JSON形式でのシリアライズ対応
- [ ] パイプラインプロファイル管理
  - [ ] 事前定義プロファイルの実装
  - [ ] ユーザー定義プロファイルの保存・読み込み
- [ ] パフォーマンス最適化
  - [ ] 並列処理の検討
  - [ ] キャッシュ戦略の実装
- [ ] 単体テストの作成
  - [ ] 基本パイプラインのテスト
  - [ ] 複雑なパイプラインのテスト

## インターフェース設計案
```csharp
namespace Baketa.Core.Abstractions.Imaging.Pipeline
{
    /// <summary>
    /// 画像処理パイプラインのインターフェース
    /// </summary>
    public interface IImagePipeline
    {
        /// <summary>
        /// パイプラインの名前
        /// </summary>
        string Name { get; }
        
        /// <summary>
        /// パイプラインの説明
        /// </summary>
        string Description { get; }
        
        /// <summary>
        /// パイプラインに登録されているフィルター
        /// </summary>
        IReadOnlyList<IImageFilter> Filters { get; }
        
        /// <summary>
        /// パイプラインを実行します
        /// </summary>
        /// <param name="inputImage">入力画像</param>
        /// <returns>処理後の画像</returns>
        Task<IAdvancedImage> ExecuteAsync(IAdvancedImage inputImage);
        
        /// <summary>
        /// パイプラインの設定を取得します
        /// </summary>
        /// <returns>パイプライン設定</returns>
        ImagePipelineSettings GetSettings();
        
        /// <summary>
        /// パイプラインの設定を適用します
        /// </summary>
        /// <param name="settings">パイプライン設定</param>
        void ApplySettings(ImagePipelineSettings settings);
    }
    
    /// <summary>
    /// パイプライン設定を表すクラス
    /// </summary>
    public class ImagePipelineSettings
    {
        /// <summary>
        /// パイプライン名
        /// </summary>
        public string Name { get; set; } = string.Empty;
        
        /// <summary>
        /// パイプライン説明
        /// </summary>
        public string Description { get; set; } = string.Empty;
        
        /// <summary>
        /// フィルター設定リスト
        /// </summary>
        public List<FilterSettings> Filters { get; set; } = new List<FilterSettings>();
    }
    
    /// <summary>
    /// フィルター設定を表すクラス
    /// </summary>
    public class FilterSettings
    {
        /// <summary>
        /// フィルタータイプ名
        /// </summary>
        public string TypeName { get; set; } = string.Empty;
        
        /// <summary>
        /// フィルターパラメータ
        /// </summary>
        public Dictionary<string, object> Parameters { get; set; } = new Dictionary<string, object>();
    }
    
    /// <summary>
    /// パイプラインビルダーインターフェース
    /// </summary>
    public interface IImagePipelineBuilder
    {
        /// <summary>
        /// パイプラインに名前を設定します
        /// </summary>
        /// <param name="name">パイプライン名</param>
        /// <returns>ビルダー</returns>
        IImagePipelineBuilder WithName(string name);
        
        /// <summary>
        /// パイプラインに説明を設定します
        /// </summary>
        /// <param name="description">パイプライン説明</param>
        /// <returns>ビルダー</returns>
        IImagePipelineBuilder WithDescription(string description);
        
        /// <summary>
        /// パイプラインにフィルターを追加します
        /// </summary>
        /// <param name="filter">追加するフィルター</param>
        /// <returns>ビルダー</returns>
        IImagePipelineBuilder AddFilter(IImageFilter filter);
        
        /// <summary>
        /// パイプラインから指定位置のフィルターを削除します
        /// </summary>
        /// <param name="index">削除するフィルターのインデックス</param>
        /// <returns>ビルダー</returns>
        IImagePipelineBuilder RemoveFilterAt(int index);
        
        /// <summary>
        /// パイプラインの全フィルターをクリアします
        /// </summary>
        /// <returns>ビルダー</returns>
        IImagePipelineBuilder ClearFilters();
        
        /// <summary>
        /// 設定からパイプラインを構築します
        /// </summary>
        /// <param name="settings">パイプライン設定</param>
        /// <returns>ビルダー</returns>
        IImagePipelineBuilder FromSettings(ImagePipelineSettings settings);
        
        /// <summary>
        /// パイプラインを構築します
        /// </summary>
        /// <returns>構築されたパイプライン</returns>
        IImagePipeline Build();
    }
}
```

## パイプライン実装例
```csharp
namespace Baketa.Core.Imaging.Pipeline
{
    /// <summary>
    /// 画像処理パイプラインの実装
    /// </summary>
    public class ImagePipeline : IImagePipeline
    {
        private readonly List<IImageFilter> _filters = new();
        private readonly ILogger<ImagePipeline>? _logger;
        
        public string Name { get; private set; }
        public string Description { get; private set; }
        public IReadOnlyList<IImageFilter> Filters => _filters.AsReadOnly();
        
        public ImagePipeline(string name, string description, ILogger<ImagePipeline>? logger = null)
        {
            Name = name;
            Description = description;
            _logger = logger;
        }
        
        public async Task<IAdvancedImage> ExecuteAsync(IAdvancedImage inputImage)
        {
            if (inputImage == null)
                throw new ArgumentNullException(nameof(inputImage));
                
            _logger?.LogDebug("パイプライン '{PipelineName}' の実行を開始 ({FilterCount} フィルター)", 
                Name, _filters.Count);
            
            var stopwatch = Stopwatch.StartNew();
            var currentImage = inputImage;
            
            for (int i = 0; i < _filters.Count; i++)
            {
                var filter = _filters[i];
                _logger?.LogTrace("フィルター #{Index} '{FilterName}' を適用中...", i, filter.Name);
                
                var filterStopwatch = Stopwatch.StartNew();
                currentImage = await filter.ApplyAsync(currentImage);
                filterStopwatch.Stop();
                
                _logger?.LogTrace("フィルター '{FilterName}' を適用完了 ({ElapsedMs}ms)", 
                    filter.Name, filterStopwatch.ElapsedMilliseconds);
            }
            
            stopwatch.Stop();
            _logger?.LogDebug("パイプライン '{PipelineName}' の実行が完了 (合計: {ElapsedMs}ms)", 
                Name, stopwatch.ElapsedMilliseconds);
            
            return currentImage;
        }
        
        public ImagePipelineSettings GetSettings()
        {
            var settings = new ImagePipelineSettings
            {
                Name = Name,
                Description = Description
            };
            
            foreach (var filter in _filters)
            {
                settings.Filters.Add(new FilterSettings
                {
                    TypeName = filter.GetType().FullName ?? filter.GetType().Name,
                    Parameters = new Dictionary<string, object>(filter.GetParameters())
                });
            }
            
            return settings;
        }
        
        public void ApplySettings(ImagePipelineSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
                
            Name = settings.Name;
            Description = settings.Description;
            
            // フィルター設定の適用はIImagePipelineBuilderを使用
        }
        
        internal void AddFilter(IImageFilter filter)
        {
            _filters.Add(filter);
        }
        
        internal void RemoveFilterAt(int index)
        {
            if (index >= 0 && index < _filters.Count)
                _filters.RemoveAt(index);
        }
        
        internal void ClearFilters()
        {
            _filters.Clear();
        }
    }
    
    /// <summary>
    /// 画像処理パイプラインビルダーの実装
    /// </summary>
    public class ImagePipelineBuilder : IImagePipelineBuilder
    {
        private readonly ImagePipeline _pipeline;
        private readonly IFilterFactory _filterFactory;
        private readonly ILogger<ImagePipelineBuilder>? _logger;
        
        public ImagePipelineBuilder(IFilterFactory filterFactory, ILogger<ImagePipelineBuilder>? logger = null)
        {
            _pipeline = new ImagePipeline("新しいパイプライン", "", logger);
            _filterFactory = filterFactory ?? throw new ArgumentNullException(nameof(filterFactory));
            _logger = logger;
        }
        
        public IImagePipelineBuilder WithName(string name)
        {
            _pipeline.Name = name;
            return this;
        }
        
        public IImagePipelineBuilder WithDescription(string description)
        {
            _pipeline.Description = description;
            return this;
        }
        
        public IImagePipelineBuilder AddFilter(IImageFilter filter)
        {
            _pipeline.AddFilter(filter);
            return this;
        }
        
        public IImagePipelineBuilder RemoveFilterAt(int index)
        {
            _pipeline.RemoveFilterAt(index);
            return this;
        }
        
        public IImagePipelineBuilder ClearFilters()
        {
            _pipeline.ClearFilters();
            return this;
        }
        
        public IImagePipelineBuilder FromSettings(ImagePipelineSettings settings)
        {
            _pipeline.Name = settings.Name;
            _pipeline.Description = settings.Description;
            _pipeline.ClearFilters();
            
            foreach (var filterSetting in settings.Filters)
            {
                var filter = _filterFactory.CreateFilter(filterSetting.TypeName);
                if (filter != null)
                {
                    foreach (var param in filterSetting.Parameters)
                    {
                        filter.SetParameter(param.Key, param.Value);
                    }
                    _pipeline.AddFilter(filter);
                }
                else
                {
                    _logger?.LogWarning("フィルター '{FilterType}' を生成できませんでした", filterSetting.TypeName);
                }
            }
            
            return this;
        }
        
        public IImagePipeline Build()
        {
            return _pipeline;
        }
    }
    
    /// <summary>
    /// フィルターファクトリーインターフェース
    /// </summary>
    public interface IFilterFactory
    {
        /// <summary>
        /// タイプ名からフィルターを生成します
        /// </summary>
        /// <param name="typeName">フィルタータイプ名</param>
        /// <returns>生成されたフィルター、または生成できない場合はnull</returns>
        IImageFilter? CreateFilter(string typeName);
        
        /// <summary>
        /// 利用可能なすべてのフィルタータイプを取得します
        /// </summary>
        /// <returns>フィルタータイプ名のリスト</returns>
        IEnumerable<string> GetAvailableFilterTypes();
    }
}
```

## OCR向けパイプライン例
```csharp
// OCR向けの標準パイプラインを作成する例
public class OcrPipelineFactory
{
    private readonly IFilterFactory _filterFactory;
    private readonly IImagePipelineBuilder _pipelineBuilder;
    
    public OcrPipelineFactory(
        IFilterFactory filterFactory, 
        IImagePipelineBuilder pipelineBuilder)
    {
        _filterFactory = filterFactory;
        _pipelineBuilder = pipelineBuilder;
    }
    
    public IImagePipeline CreateStandardOcrPipeline()
    {
        return _pipelineBuilder
            .WithName("標準OCRパイプライン")
            .WithDescription("一般的なテキスト認識向けの前処理パイプライン")
            .AddFilter(_filterFactory.CreateFilter("GrayscaleFilter")!)
            .AddFilter(_filterFactory.CreateFilter("GaussianBlurFilter")!)
            .AddFilter(_filterFactory.CreateFilter("ContrastEnhancementFilter")!)
            .AddFilter(_filterFactory.CreateFilter("AdaptiveThresholdFilter")!)
            .Build();
    }
    
    public IImagePipeline CreateGameUiTextPipeline()
    {
        var pipeline = _pipelineBuilder
            .WithName("ゲームUIテキスト検出パイプライン")
            .WithDescription("ゲームUI内のテキスト検出に特化したパイプライン")
            .AddFilter(_filterFactory.CreateFilter("GrayscaleFilter")!)
            .AddFilter(_filterFactory.CreateFilter("BilateralFilter")!)
            .AddFilter(_filterFactory.CreateFilter("SharpenFilter")!)
            .AddFilter(_filterFactory.CreateFilter("OtsuThresholdFilter")!)
            .Build();
            
        // 特定のパラメータをカスタマイズ
        var otsuFilter = pipeline.Filters.LastOrDefault();
        if (otsuFilter != null)
        {
            otsuFilter.SetParameter("InvertColors", true);
        }
        
        return pipeline;
    }
}
```

## 関連Issue/参考
- 親Issue: #5 実装: 画像処理抽象化レイヤーの拡張
- 依存: #5.2 実装: 画像処理フィルターの抽象化
- 関連: #8 実装: OpenCVベースのOCR前処理最適化
- 参照: E:\dev\Baketa\docs\3-architecture\ocr-system\ocr-opencv-approach.md
- 参照: E:\dev\Baketa\docs\2-development\coding-standards\csharp-standards.md (6.3 パフォーマンス測定)

## マイルストーン
マイルストーン2: キャプチャとOCR基盤

## ラベル
- `type: feature`
- `priority: high`
- `component: core`
