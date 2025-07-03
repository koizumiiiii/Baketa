using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Imaging.Pipeline;

namespace Baketa.Infrastructure.Imaging.Pipeline;

    /// <summary>
    /// IImageFilterをIImagePipelineFilterに変換するアダプター
    /// </summary>
    public sealed class ImageFilterAdapter : IImagePipelineFilter
    {
        private readonly IImageFilter _originalFilter;
        private readonly Dictionary<string, object> _parameters = [];
        private readonly List<PipelineStepParameter> _parameterDefinitions = [];
        
        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="originalFilter">元のIImageFilterインスタンス</param>
        public ImageFilterAdapter(IImageFilter originalFilter)
        {
            _originalFilter = originalFilter ?? throw new ArgumentNullException(nameof(originalFilter));
            InitializeParameterDefinitions();
        }
        
        /// <summary>
        /// フィルター名
        /// </summary>
        public string Name => _originalFilter.Name;
        
        /// <summary>
        /// フィルター説明
        /// </summary>
        public string Description => _originalFilter.Description;
        
        /// <summary>
        /// フィルターカテゴリ
        /// </summary>
        public string Category => _originalFilter.Category.ToString();
        
        // IImagePipelineFilterインターフェースにはParametersプロパティが存在しないため、この実装は必要ない
        
        // 公開アクセス用のParametersプロパティも不要なため削除
        
        /// <summary>
        /// パラメータ定義リスト (IImagePipelineStep用)
        /// </summary>
        public IReadOnlyCollection<PipelineStepParameter> Parameters => _parameterDefinitions.AsReadOnly();
        
        /// <summary>
        /// エラーハンドリング戦略
        /// </summary>
        public StepErrorHandlingStrategy ErrorHandlingStrategy { get; set; } = StepErrorHandlingStrategy.SkipStep;
        
        /// <summary>
        /// フィルター適用
        /// </summary>
        public Task<IAdvancedImage> ApplyAsync(IAdvancedImage inputImage)
        {
            return _originalFilter.ApplyAsync(inputImage);
        }
        
        /// <summary>
        /// パイプラインステップの実行
        /// </summary>
        public Task<IAdvancedImage> ExecuteAsync(IAdvancedImage input, PipelineContext context, CancellationToken cancellationToken = default)
        {
            // キャンセル確認
            cancellationToken.ThrowIfCancellationRequested();
            
            // 単純に_originalFilter.ApplyAsync()を呼び出す
            return _originalFilter.ApplyAsync(input);
        }
        
        /// <summary>
        /// パラメータのリセット
        /// </summary>
        public void ResetParameters()
        {
            _originalFilter.ResetParameters();
            _parameters.Clear();
            
            // デフォルト値で初期化
            foreach (var param in _parameterDefinitions)
            {
                if (param.DefaultValue != null)
                {
                    _parameters[param.Name] = param.DefaultValue;
                }
            }
        }
        
        /// <summary>
        /// パラメータの取得
        /// </summary>
        public IDictionary<string, object> GetParameters()
        {
            // 元のフィルターからパラメータを取得
            var originalParams = _originalFilter.GetParameters();
            
            // 新しいDictionaryを作成して返す
            return originalParams.ToDictionary(param => param.Key, param => param.Value);
        }
        
        /// <summary>
        /// パラメータの設定
        /// </summary>
        public void SetParameter(string parameterName, object value)
        {
            _originalFilter.SetParameter(parameterName, value);
            _parameters[parameterName] = value;
        }
        
        /// <summary>
        /// フォーマット対応確認
        /// </summary>
        public bool SupportsFormat(ImageFormat format)
        {
            return _originalFilter.SupportsFormat(format);
        }
        
        /// <summary>
        /// 出力画像情報の取得
        /// </summary>
        public PipelineImageInfo GetOutputImageInfo(IAdvancedImage input)
        {
            // 元のフィルターから画像情報を取得し、PipelineImageInfoに変換
            var imageInfo = _originalFilter.GetOutputImageInfo(input);
            return PipelineImageInfo.FromImageInfo(imageInfo, PipelineStage.Processing);
        }
        
        /// <summary>
        /// パラメータ値を取得します
        /// </summary>
        public object GetParameter(string parameterName)
        {
            // まず内部パラメータを確認
            if (_parameters.TryGetValue(parameterName, out var value))
            {
                return value;
            }
            
            // 存在しない場合は元のフィルターから取得を試みる
            try
            {
                var getMethod = _originalFilter.GetType().GetMethod("GetParameter") ?? throw new InvalidOperationException($"メソッド'GetParameter'が{_originalFilter.GetType().Name}に見つかりません");
                var originalParam = getMethod.Invoke(_originalFilter, [parameterName]);
                return originalParam ?? throw new KeyNotFoundException($"パラメータ'{parameterName}'が見つかりません");
            }
            catch (InvalidOperationException ex)
            {
                // メソッド呼び出しエラー
                throw new InvalidOperationException($"パラメータ'{parameterName}'の取得中にエラーが発生しました", ex);
            }
            catch (ArgumentException ex)
            {
                // 引数例外
                throw new ArgumentException($"パラメータ'{parameterName}'の取得中に引数エラーが発生しました", ex);
            }
            catch (TargetInvocationException ex)
            {
                // 反映呼び出し例外
                throw new InvalidOperationException($"パラメータ'{parameterName}'の取得中にエラーが発生しました", ex.InnerException ?? ex);
            }
            
            throw new KeyNotFoundException($"パラメータ'{parameterName}'が見つかりません");
        }
        
        /// <summary>
        /// パラメータ値をジェネリック型で取得します
        /// </summary>
        public T GetParameter<T>(string parameterName)
        {
            var value = GetParameter(parameterName);
            
            try
            {
                return value != null 
                    ? (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture)
                    : throw new InvalidOperationException($"パラメータ'{parameterName}'がnullです");
            }
            catch (Exception ex)
            {
                throw new InvalidCastException($"パラメータ'{parameterName}'を型'{typeof(T).Name}'に変換できません", ex);
            }
        }
        
        /// <summary>
        /// パラメータ定義を初期化します
        /// </summary>
        private void InitializeParameterDefinitions()
        {
            // 元のフィルターのパラメータから定義を作成
            var originalParams = _originalFilter.GetParameters();
            
            _parameterDefinitions.Clear();
            foreach (var param in originalParams)
            {
                var type = param.Value?.GetType() ?? typeof(object);
                
                // パラメータ定義を追加
                _parameterDefinitions.Add(new PipelineStepParameter(
                    name: param.Key, 
                    description: $"{param.Key} parameter",
                    parameterType: type,
                    defaultValue: param.Value));
                
                // パラメータ値の初期化
                if (param.Value != null)
                {
                    _parameters[param.Key] = param.Value;
                }
                else if (type.IsValueType)
                {
                    // Value Typeの場合はデフォルトインスタンスを生成
                    _parameters[param.Key] = Activator.CreateInstance(type)!;
                }
                // nullの場合はディクショナリに加えない
            }
        }
    }
