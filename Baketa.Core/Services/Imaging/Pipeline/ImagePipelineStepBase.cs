using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Imaging.Pipeline;
using Microsoft.Extensions.Logging;

#pragma warning disable CA2208 // ArgumentExceptionを正しくインスタンス化する

namespace Baketa.Core.Services.Imaging.Pipeline;
    /// <summary>
    /// パイプラインステップの基底クラス
    /// </summary>
    public abstract class ImagePipelineStepBase : IImagePipelineStep
    {
        private readonly Dictionary<string, object> _parameters = [];
        private readonly List<PipelineStepParameter> _parameterDefinitions = [];
        
        /// <summary>
        /// ステップの名前
        /// </summary>
        public abstract string Name { get; }
        
        /// <summary>
        /// ステップの説明
        /// </summary>
        public abstract string Description { get; }
        
        /// <summary>
        /// ステップのパラメータ定義
        /// </summary>
        public IReadOnlyCollection<PipelineStepParameter> Parameters => _parameterDefinitions;
        
        /// <summary>
        /// ステップのエラーハンドリング戦略
        /// </summary>
        public StepErrorHandlingStrategy ErrorHandlingStrategy { get; set; } = StepErrorHandlingStrategy.StopExecution;
        
        /// <summary>
        /// コンストラクター
        /// </summary>
        protected ImagePipelineStepBase()
        {
            // 注意: 派生クラスではコンストラクタでInitializeParametersを明示的に呼び出す必要があります
            // コンストラクタでは呼び出さないことでCA2214警告を回避
        }
        
        /// <summary>
        /// ステップを実行します
        /// </summary>
        /// <param name="input">入力画像</param>
        /// <param name="context">パイプライン実行コンテキスト</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>処理結果画像</returns>
        public virtual async Task<IAdvancedImage> ExecuteAsync(IAdvancedImage input, PipelineContext context, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(context);
            
            try
            {
                // キャンセル要求チェック
                if (cancellationToken.IsCancellationRequested)
                {
                    context.Logger.LogInformation("ステップ '{StepName}' の実行がキャンセルされました", Name);
                    cancellationToken.ThrowIfCancellationRequested();
                }
                
                // 実行開始イベント通知
                await context.EventListener.OnStepStartAsync(this, input, context).ConfigureAwait(false);
                
                // ステップの具体的な処理を実行
                var result = await ProcessAsync(input, context, cancellationToken).ConfigureAwait(false);
                
                // 実行完了イベント通知
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
                context.Logger.LogError(ex, "ステップ '{StepName}' の実行中にエラーが発生しました", Name);
                
                // エラーハンドリング戦略に基づいた処理
                switch (ErrorHandlingStrategy)
                {
                    case StepErrorHandlingStrategy.StopExecution:
                        throw; // 例外をそのまま伝播
                        
                    case StepErrorHandlingStrategy.SkipStep:
                        context.Logger.LogWarning("ステップ '{StepName}' はスキップされました", Name);
                        return input; // 入力をそのまま返す
                        
                    case StepErrorHandlingStrategy.UseFallback:
                        // イベントハンドラーのフォールバック処理を使用
                        var fallbackResult = await context.EventListener.OnStepErrorAsync(this, ex, context).ConfigureAwait(false);
                        return fallbackResult ?? input;
                        
                    case StepErrorHandlingStrategy.LogAndContinue:
                        context.Logger.LogWarning("ステップ '{StepName}' でエラーが発生しましたが、処理を継続します", Name);
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
        public virtual void SetParameter(string parameterName, object value)
        {
            if (string.IsNullOrEmpty(parameterName))
            {
                throw new ArgumentException($"パラメータ名が空またはnullです。", nameof(parameterName));
            }

            // パラメータの定義を取得
            var paramDef = _parameterDefinitions.FirstOrDefault(p => p.Name == parameterName)
                ?? throw new ArgumentException($"パラメータ '{parameterName}' はこのステップでは定義されていません。", nameof(parameterName));
            
            // パラメータの値を検証
            if (!paramDef.ValidateValue(value))
            {
                throw new ArgumentException($"パラメータ '{parameterName}' に対して無効な値が指定されました。", nameof(value));
            }
            
            // パラメータを設定
            _parameters[parameterName] = value;
        }
        
        /// <summary>
        /// パラメータ値を取得します
        /// </summary>
        /// <param name="parameterName">パラメータ名</param>
        /// <returns>パラメータ値</returns>
        public virtual object GetParameter(string parameterName)
        {
            if (string.IsNullOrEmpty(parameterName))
            {
                throw new ArgumentException($"パラメータ名が空またはnullです。", nameof(parameterName));
            }
            
            if (!_parameters.TryGetValue(parameterName, out var value))
            {
                // パラメータが設定されていない場合、デフォルト値を取得
                var paramDef = _parameterDefinitions.FirstOrDefault(p => p.Name == parameterName)
                    ?? throw new ArgumentException($"パラメータ '{parameterName}' はこのステップでは定義されていません。", nameof(parameterName));
                
                return paramDef.DefaultValue!;
            }
            
            return value;
        }
        
        /// <summary>
        /// パラメータ値をジェネリック型で取得します
        /// </summary>
        /// <typeparam name="T">取得する型</typeparam>
        /// <param name="parameterName">パラメータ名</param>
        /// <returns>パラメータ値</returns>
#pragma warning disable IDE0270 // nullチェックの簡素化
        public virtual T GetParameter<T>(string parameterName)
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
        public virtual PipelineImageInfo GetOutputImageInfo(IAdvancedImage input)
        {
            ArgumentNullException.ThrowIfNull(input);
            
            // デフォルトでは入力と同じ画像情報を返す
            // 派生クラスでオーバーライドすることを期待
            var imageInfo = new Baketa.Core.Abstractions.Imaging.ImageInfo
            {
                Width = input.Width,
                Height = input.Height,
                Format = input.Format,
                Channels = GetChannelCount(input.Format)
            };
            
            return PipelineImageInfo.FromImageInfo(imageInfo, PipelineStage.Processing);
        }
        
        /// <summary>
        /// ステップの処理を実行します
        /// </summary>
        /// <param name="input">入力画像</param>
        /// <param name="context">パイプライン実行コンテキスト</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>処理結果画像</returns>
        protected abstract Task<IAdvancedImage> ProcessAsync(IAdvancedImage input, PipelineContext context, CancellationToken cancellationToken);
        
        /// <summary>
        /// パラメータを初期化します
        /// </summary>
        protected abstract void InitializeParameters();
        
        /// <summary>
        /// パラメータを登録します
        /// </summary>
        /// <param name="name">パラメータ名</param>
        /// <param name="description">パラメータの説明</param>
        /// <param name="type">パラメータの型</param>
        /// <param name="defaultValue">デフォルト値</param>
        /// <param name="minValue">最小値（数値パラメータの場合）</param>
        /// <param name="maxValue">最大値（数値パラメータの場合）</param>
        /// <param name="options">選択肢（列挙型パラメータの場合）</param>
        protected void RegisterParameter(
            string name,
            string description,
            Type type,
            object? defaultValue,
            object? minValue = null,
            object? maxValue = null,
            IReadOnlyCollection<object>? options = null)
        {
            var parameter = new PipelineStepParameter(
                name,
                description,
                type,
                defaultValue,
                minValue,
                maxValue,
                options);
            
            _parameterDefinitions.Add(parameter);
            
            // デフォルト値が指定されていれば設定
            if (defaultValue != null)
            {
                _parameters[name] = defaultValue;
            }
        }
        
        /// <summary>
        /// パラメータを登録します（ジェネリック版）
        /// </summary>
        /// <typeparam name="T">パラメータの型</typeparam>
        /// <param name="name">パラメータ名</param>
        /// <param name="description">パラメータの説明</param>
        /// <param name="defaultValue">デフォルト値</param>
        /// <param name="minValue">最小値（数値パラメータの場合）</param>
        /// <param name="maxValue">最大値（数値パラメータの場合）</param>
        /// <param name="options">選択肢（列挙型パラメータの場合）</param>
        protected void RegisterParameter<T>(
            string name,
            string description,
            T defaultValue,
            T? minValue = default,
            T? maxValue = default,
            IReadOnlyCollection<T>? options = null)
        {
            IReadOnlyCollection<object>? objectOptions = null;
            
            if (options != null)
            {
                objectOptions = [.. options.Cast<object>()];
            }
            
            RegisterParameter(
                name,
                description,
                typeof(T),
                defaultValue,
                minValue as object,
                maxValue as object,
                objectOptions);
        }
        
        /// <summary>
        /// 画像フォーマットからチャンネル数を取得します
        /// </summary>
        /// <param name="format">画像フォーマット</param>
        /// <returns>チャンネル数</returns>
        protected static int GetChannelCount(ImageFormat format)
        {
            return format switch
            {
                ImageFormat.Rgb24 => 3,
                ImageFormat.Rgba32 => 4,
                ImageFormat.Grayscale8 => 1,
                ImageFormat.Png => 4,
                ImageFormat.Jpeg => 3,
                ImageFormat.Bmp => 3,
                _ => throw new ArgumentException($"未サポートの画像フォーマット: {format}", nameof(format))
            };
        }
    }

#pragma warning restore CA2208
