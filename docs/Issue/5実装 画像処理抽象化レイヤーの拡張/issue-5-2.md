# 実装: 画像処理フィルターの抽象化

## 概要
再利用可能な画像処理フィルターのための抽象化レイヤーを設計・実装します。

## 目的・理由
様々な画像処理アルゴリズムを統一したインターフェースで利用できるようにすることで、処理の組み合わせや入れ替えが容易になり、OCR前処理の柔軟なカスタマイズが可能になります。また、プラットフォーム固有の実装（OpenCVなど）との連携も容易になります。

## 詳細
- `IImageFilter`インターフェースの設計と実装
- 基本的な画像フィルターの実装（グレースケール、ガウシアンぼかし、二値化など）
- フィルターチェーンの実装
- フィルターパラメータの柔軟な設定機構

## タスク分解
- [ ] `IImageFilter`インターフェースの設計
  - [ ] 基本メソッド定義
  - [ ] パラメータ管理方法の定義
- [ ] フィルターカテゴリの設計
  - [ ] 色調変換フィルター
  - [ ] ノイズ除去フィルター
  - [ ] エッジ検出フィルター
  - [ ] 形態学的フィルター
- [ ] 基本フィルターの実装
  - [ ] グレースケールフィルター
  - [ ] ガウシアンぼかしフィルター
  - [ ] ソーベルエッジ検出フィルター
  - [ ] 明度・コントラスト調整フィルター
  - [ ] 閾値処理（二値化）フィルター
  - [ ] モルフォロジー処理フィルター
- [ ] フィルターチェーンの実装
  - [ ] 複数フィルターの連結メカニズム
  - [ ] パラメータの連携
- [ ] フィルター設定の実装
  - [ ] パラメータクラスの設計
  - [ ] シリアライズ可能な設定
- [ ] 単体テストの作成

## インターフェース設計案
```csharp
namespace Baketa.Core.Abstractions.Imaging.Filters
{
    /// <summary>
    /// 画像フィルターを表すインターフェース
    /// </summary>
    public interface IImageFilter
    {
        /// <summary>
        /// フィルターの名前
        /// </summary>
        string Name { get; }
        
        /// <summary>
        /// フィルターの説明
        /// </summary>
        string Description { get; }
        
        /// <summary>
        /// フィルターのカテゴリ
        /// </summary>
        FilterCategory Category { get; }
        
        /// <summary>
        /// 画像にフィルターを適用します
        /// </summary>
        /// <param name="inputImage">入力画像</param>
        /// <returns>フィルター適用後の新しい画像</returns>
        Task<IAdvancedImage> ApplyAsync(IAdvancedImage inputImage);
        
        /// <summary>
        /// フィルターのパラメータをリセットします
        /// </summary>
        void ResetParameters();
        
        /// <summary>
        /// フィルターの現在のパラメータを取得します
        /// </summary>
        /// <returns>パラメータディクショナリ</returns>
        IDictionary<string, object> GetParameters();
        
        /// <summary>
        /// フィルターのパラメータを設定します
        /// </summary>
        /// <param name="name">パラメータ名</param>
        /// <param name="value">パラメータ値</param>
        void SetParameter(string name, object value);
    }
    
    /// <summary>
    /// フィルターカテゴリを表す列挙型
    /// </summary>
    public enum FilterCategory
    {
        /// <summary>
        /// 色調変換
        /// </summary>
        ColorAdjustment,
        
        /// <summary>
        /// ぼかし・ノイズ除去
        /// </summary>
        Blur,
        
        /// <summary>
        /// シャープ化
        /// </summary>
        Sharpen,
        
        /// <summary>
        /// エッジ検出
        /// </summary>
        EdgeDetection,
        
        /// <summary>
        /// 二値化
        /// </summary>
        Threshold,
        
        /// <summary>
        /// 形態学的処理
        /// </summary>
        Morphology,
        
        /// <summary>
        /// 特殊効果
        /// </summary>
        Effect,
        
        /// <summary>
        /// 複合フィルター
        /// </summary>
        Composite
    }
    
    /// <summary>
    /// 画像フィルターの基底クラス
    /// </summary>
    public abstract class ImageFilterBase : IImageFilter
    {
        private readonly Dictionary<string, object> _parameters = new();
        
        public abstract string Name { get; }
        public abstract string Description { get; }
        public abstract FilterCategory Category { get; }
        
        public abstract Task<IAdvancedImage> ApplyAsync(IAdvancedImage inputImage);
        
        public virtual void ResetParameters()
        {
            _parameters.Clear();
            InitializeDefaultParameters();
        }
        
        public IDictionary<string, object> GetParameters()
        {
            return new Dictionary<string, object>(_parameters);
        }
        
        public virtual void SetParameter(string name, object value)
        {
            if (!_parameters.ContainsKey(name))
                throw new ArgumentException($"パラメータ '{name}' はこのフィルターでは定義されていません。");
                
            _parameters[name] = value;
        }
        
        protected object GetParameterValue(string name)
        {
            if (!_parameters.TryGetValue(name, out var value))
                throw new ArgumentException($"パラメータ '{name}' はこのフィルターでは定義されていません。");
                
            return value;
        }
        
        protected T GetParameterValue<T>(string name)
        {
            var value = GetParameterValue(name);
            if (value is T typedValue)
                return typedValue;
                
            throw new InvalidCastException($"パラメータ '{name}' は型 {typeof(T).Name} に変換できません。");
        }
        
        protected void RegisterParameter(string name, object defaultValue)
        {
            _parameters[name] = defaultValue;
        }
        
        protected abstract void InitializeDefaultParameters();
    }
    
    /// <summary>
    /// フィルターチェーンを表すクラス
    /// </summary>
    public class FilterChain : IImageFilter
    {
        private readonly List<IImageFilter> _filters = new();
        
        public string Name => "フィルターチェーン";
        public string Description => "複数のフィルターを順番に適用します";
        public FilterCategory Category => FilterCategory.Composite;
        
        public void AddFilter(IImageFilter filter)
        {
            _filters.Add(filter);
        }
        
        public void RemoveFilter(IImageFilter filter)
        {
            _filters.Remove(filter);
        }
        
        public void ClearFilters()
        {
            _filters.Clear();
        }
        
        public async Task<IAdvancedImage> ApplyAsync(IAdvancedImage inputImage)
        {
            var currentImage = inputImage;
            
            foreach (var filter in _filters)
            {
                currentImage = await filter.ApplyAsync(currentImage);
            }
            
            return currentImage;
        }
        
        public void ResetParameters()
        {
            foreach (var filter in _filters)
            {
                filter.ResetParameters();
            }
        }
        
        public IDictionary<string, object> GetParameters()
        {
            // フィルターチェーンでは個別フィルターのパラメータを直接公開しない
            return new Dictionary<string, object>();
        }
        
        public void SetParameter(string name, object value)
        {
            // フィルターチェーンでは個別フィルターのパラメータを直接設定しない
            throw new NotSupportedException("フィルターチェーンでは個別のパラメータを直接設定できません。");
        }
    }
}
```

## フィルター実装例
```csharp
namespace Baketa.Core.Imaging.Filters
{
    /// <summary>
    /// グレースケール変換フィルター
    /// </summary>
    public class GrayscaleFilter : ImageFilterBase
    {
        public override string Name => "グレースケール";
        public override string Description => "画像をグレースケールに変換します";
        public override FilterCategory Category => FilterCategory.ColorAdjustment;
        
        protected override void InitializeDefaultParameters()
        {
            // このフィルターにはパラメータがない
        }
        
        public override async Task<IAdvancedImage> ApplyAsync(IAdvancedImage inputImage)
        {
            return await inputImage.ToGrayscaleAsync();
        }
    }
    
    /// <summary>
    /// ガウシアンぼかしフィルター
    /// </summary>
    public class GaussianBlurFilter : ImageFilterBase
    {
        public override string Name => "ガウシアンぼかし";
        public override string Description => "ガウシアン関数を用いたぼかしを適用します";
        public override FilterCategory Category => FilterCategory.Blur;
        
        protected override void InitializeDefaultParameters()
        {
            RegisterParameter("KernelSize", 3);  // カーネルサイズ（3x3, 5x5, 7x7, ...）
            RegisterParameter("Sigma", 1.0);     // 標準偏差
        }
        
        public override async Task<IAdvancedImage> ApplyAsync(IAdvancedImage inputImage)
        {
            int kernelSize = GetParameterValue<int>("KernelSize");
            double sigma = GetParameterValue<double>("Sigma");
            
            // 実際のガウシアンぼかし処理を実装
            // このサンプルでは実装の詳細は省略
            
            // 仮実装：結果として同じ画像を返す
            return inputImage;
        }
    }
}
```

## 関連Issue/参考
- 親Issue: #5 実装: 画像処理抽象化レイヤーの拡張
- 関連: #8 実装: OpenCVベースのOCR前処理最適化
- 参照: E:\dev\Baketa\docs\3-architecture\ocr-system\ocr-opencv-approach.md
- 参照: E:\dev\Baketa\docs\2-development\coding-standards\csharp-standards.md (5.3 依存性注入と疎結合)

## マイルストーン
マイルストーン2: キャプチャとOCR基盤

## ラベル
- `type: feature`
- `priority: high`
- `component: core`
