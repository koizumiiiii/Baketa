using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using CoreImageFilter = Baketa.Core.Abstractions.Imaging.IImageFilter;
using Baketa.Core.Abstractions.Imaging.Pipeline;
using Microsoft.Extensions.Logging;

#pragma warning disable CA2208 // ArgumentExceptionを正しくインスタンス化する

namespace Baketa.Core.Services.Imaging.Pipeline;

    /// <summary>
    /// Baketa.Core.Abstractions.Imaging.IImageFilterをIImagePipelineStepに適応させるアダプター
    /// </summary>
    public class FilterPipelineStepAdapter : IImagePipelineStep
    {
        private readonly CoreImageFilter _filter;
        private readonly List<PipelineStepParameter> _parameterDefinitions = new();
        private readonly ILogger _logger;
        
        /// <summary>
        /// ステップの名前
        /// </summary>
        public string Name => _filter.Name;
        
        /// <summary>
        /// ステップの説明
        /// </summary>
        public string Description => _filter.Description;
        
        /// <summary>
        /// ステップのパラメータ定義
        /// </summary>
        public IReadOnlyCollection<PipelineStepParameter> Parameters => _parameterDefinitions;
        
        /// <summary>
        /// ステップのエラーハンドリング戦略
        /// </summary>
        public StepErrorHandlingStrategy ErrorHandlingStrategy { get; set; } = StepErrorHandlingStrategy.StopExecution;

        /// <summary>
        /// 新しいFilterPipelineStepAdapterを作成します
        /// </summary>
        /// <param name="filter">適応させるフィルター</param>
        /// <param name="logger">ロガー</param>
        public FilterPipelineStepAdapter(CoreImageFilter filter, ILogger logger)
        {
            _filter = filter ?? throw new ArgumentNullException(nameof(filter));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // フィルターのパラメータからパイプラインステップのパラメータを作成
            CreateParametersFromFilter();
        }
        
        /// <summary>
        /// 新しいFilterPipelineStepAdapterを作成します
        /// </summary>
        /// <param name="filter">適応させるフィルター</param>
        public FilterPipelineStepAdapter(CoreImageFilter filter) : this(filter, new Microsoft.Extensions.Logging.Abstractions.NullLogger<FilterPipelineStepAdapter>())
        {
        }
        
        /// <summary>
        /// ステップを実行します
        /// </summary>
        /// <param name="input">入力画像</param>
        /// <param name="context">パイプライン実行コンテキスト</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>処理結果画像</returns>
        public async Task<IAdvancedImage> ExecuteAsync(IAdvancedImage input, PipelineContext context, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(context);
            
            // 開始イベントを通知
            await context.EventListener.OnStepStartAsync(this, input, context).ConfigureAwait(false);
            
            try
            {
                // キャンセルチェック
                if (cancellationToken.IsCancellationRequested)
                {
                    context.Logger.LogInformation("フィルター '{FilterName}' の適用がキャンセルされました", Name);
                    cancellationToken.ThrowIfCancellationRequested();
                }
                
                // フィルターを適用
                var result = await _filter.ApplyAsync(input).ConfigureAwait(false);
                
                // 完了イベントを通知
                await context.EventListener.OnStepCompleteAsync(this, result, context, 0).ConfigureAwait(false);
                
                return result;
            }
            catch (OperationCanceledException)
            {
                // キャンセルの場合はそのまま伝播
                throw;
            }
            catch (Exception ex)
            {
                context.Logger.LogError(ex, "フィルター '{FilterName}' の適用中にエラーが発生しました", Name);
                
                // エラーハンドリング戦略に基づいた処理
                switch (ErrorHandlingStrategy)
                {
                    case StepErrorHandlingStrategy.StopExecution:
                        throw; // 例外をそのまま伝播
                        
                    case StepErrorHandlingStrategy.SkipStep:
                        context.Logger.LogWarning("フィルター '{FilterName}' はスキップされました", Name);
                        return input; // 入力をそのまま返す
                        
                    case StepErrorHandlingStrategy.UseFallback:
                        // イベントハンドラーのフォールバック処理を使用
                        var fallbackResult = await context.EventListener.OnStepErrorAsync(this, ex, context).ConfigureAwait(false);
                        return fallbackResult ?? input;
                        
                    case StepErrorHandlingStrategy.LogAndContinue:
                        context.Logger.LogWarning("フィルター '{FilterName}' でエラーが発生しましたが、処理を継続します", Name);
                        return input;
                        
                    default:
                        var errorMsg = $"不明なエラーハンドリング戦略: {ErrorHandlingStrategy}";
                        throw new ArgumentOutOfRangeException(nameof(ErrorHandlingStrategy), ErrorHandlingStrategy, errorMsg);
                }
            }
        }

        /// <summary>
        /// パラメータ値を設定します
        /// </summary>
        /// <param name="parameterName">パラメータ名</param>
        /// <param name="value">設定する値</param>
        public void SetParameter(string parameterName, object value)
        {
            if (string.IsNullOrEmpty(parameterName))
            {
                throw new ArgumentException($"パラメータ名が空またはnullです。", nameof(parameterName));
            }
            
            // パラメータ定義をチェック
            var paramDef = _parameterDefinitions.FirstOrDefault(p => p.Name == parameterName)
                ?? throw new ArgumentException($"パラメータ '{parameterName}' はこのステップでは定義されていません。", nameof(parameterName));
            
            // パラメータ値を検証
            if (!paramDef.ValidateValue(value))
            {
                throw new ArgumentException($"パラメータ '{parameterName}' に対して無効な値が指定されました。", nameof(value));
            }
            
            // フィルターにパラメータを設定
            _filter.SetParameter(parameterName, value);
        }

        /// <summary>
        /// パラメータ値を取得します
        /// </summary>
        /// <param name="parameterName">パラメータ名</param>
        /// <returns>パラメータ値</returns>
        public object GetParameter(string parameterName)
        {
            if (string.IsNullOrEmpty(parameterName))
            {
                throw new ArgumentException($"パラメータ名が空またはnullです。", nameof(parameterName));
            }
            
            // フィルターからパラメータを取得
            try
            {
                return _filter.GetParameters()[parameterName];
            }
            catch (KeyNotFoundException)
            {
                throw new ArgumentException($"パラメータ '{parameterName}' はこのステップでは定義されていません。", nameof(parameterName));
            }
        }

        /// <summary>
        /// パラメータ値をジェネリック型で取得します
        /// </summary>
        /// <typeparam name="T">取得する型</typeparam>
        /// <param name="parameterName">パラメータ名</param>
        /// <returns>パラメータ値</returns>
#pragma warning disable IDE0270 // nullチェックの簡素化
        public T GetParameter<T>(string parameterName)
        {
            var value = GetParameter(parameterName);
            
            if (value is T typedValue)
                return typedValue;
            
            throw new InvalidCastException($"パラメータ '{parameterName}' の値を型 {typeof(T).Name} に変換できません。");
        }
#pragma warning restore IDE0270

        /// <summary>
        /// 出力画像情報を取得します
        /// </summary>
        /// <param name="input">入力画像</param>
        /// <returns>出力画像の情報</returns>
        public PipelineImageInfo GetOutputImageInfo(IAdvancedImage input)
        {
            ArgumentNullException.ThrowIfNull(input);
            
            // フィルターの出力情報を取得
            var imageInfo = _filter.GetOutputImageInfo(input);
            
            // ImageInfo を PipelineImageInfo に変換
            return new PipelineImageInfo(
                imageInfo.Width,
                imageInfo.Height,
                imageInfo.Channels,
                imageInfo.Format,
                PipelineStage.Processing
            );
        }

        /// <summary>
        /// フィルターのパラメータからパイプラインステップのパラメータを作成します
        /// </summary>
        private void CreateParametersFromFilter()
        {
            // nullチェックを追加
            var filterParams = _filter.GetParameters() ?? new Dictionary<string, object>();
            
            foreach (var param in filterParams)
            {
                // フィルターパラメータの型を推定
                // param.Valueがnullの場合はobject型を使用
                var paramType = param.Value?.GetType() ?? typeof(object);
                
                // パラメータを登録
                _parameterDefinitions.Add(new PipelineStepParameter(
                    param.Key,
                    $"{param.Key} パラメータ", // 適切な説明がない場合のフォールバック
                    paramType,
                    param.Value)); // nullも許容
            }
        }
    }

#pragma warning restore CA2208
