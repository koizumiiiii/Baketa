using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Imaging.Pipeline;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Imaging.Filters;

    /// <summary>
    /// 画像フィルター実装の基底クラス
    /// </summary>
    public abstract class BaseImageFilter : IImageFilter, IImagePipelineStep
    {
        private readonly Dictionary<string, object> _parameters = [];
        private readonly ILogger? _logger;
        
        /// <summary>
        /// フィルターの名前
        /// </summary>
        public abstract string Name { get; }
        
        /// <summary>
        /// フィルターの説明
        /// </summary>
        public abstract string Description { get; }
        
        /// <summary>
        /// フィルターのカテゴリ
        /// </summary>
        public virtual FilterCategory Category => FilterCategory.ColorAdjustment;
        
        /// <summary>
        /// ステップのパラメータ定義 (IImagePipelineStep用)
        /// </summary>
        public virtual IReadOnlyCollection<PipelineStepParameter> Parameters => GetParameterDefinitions();
        
        /// <summary>
        /// フィルターのエラー処理戦略
        /// </summary>
        public StepErrorHandlingStrategy ErrorHandlingStrategy { get; set; } = StepErrorHandlingStrategy.ContinueExecution;
        
        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="logger">ロガー</param>
        protected BaseImageFilter(ILogger? logger = null)
        {
            _logger = logger;
            // 以下は仮想メソッドを呼び出しているため、直接呼び出さずに継承先で実装するように修正
            // InitializeDefaultParameters();
        }
        
        /// <summary>
        /// パラメータをリセットします
        /// </summary>
        public virtual void ResetParameters()
        {
            _parameters.Clear();
            InitializeDefaultParameters();
        }
        
        /// <summary>
        /// デフォルトパラメータを初期化します
        /// </summary>
        protected virtual void InitializeDefaultParameters()
        {
            // サブクラスでオーバーライドして実装
        }
        
        /// <summary>
        /// パラメータ定義を取得します
        /// </summary>
        /// <returns>パラメータ定義のコレクション</returns>
        protected virtual IReadOnlyCollection<PipelineStepParameter> GetParameterDefinitions()
        {
        return [];
        }
        
        /// <summary>
        /// パラメータ値を設定します
        /// </summary>
        /// <param name="parameterName">パラメータ名</param>
        /// <param name="value">設定する値</param>
        public virtual void SetParameter(string parameterName, object value)
        {
            ArgumentNullException.ThrowIfNullOrEmpty(parameterName, nameof(parameterName));
                
            _parameters[parameterName] = value;
        }
        
        /// <summary>
        /// パラメータ値を取得します
        /// </summary>
        /// <param name="parameterName">パラメータ名</param>
        /// <returns>パラメータ値</returns>
        public virtual object GetParameter(string parameterName)
        {
            ArgumentNullException.ThrowIfNullOrEmpty(parameterName, nameof(parameterName));
                
            if (_parameters.TryGetValue(parameterName, out var value))
                return value;
                
            throw new KeyNotFoundException($"パラメータ '{parameterName}' が見つかりません");
        }
        
        /// <summary>
        /// パラメータ値をジェネリック型で取得します
        /// </summary>
        /// <typeparam name="T">取得する型</typeparam>
        /// <param name="parameterName">パラメータ名</param>
        /// <returns>パラメータ値</returns>
        public virtual T GetParameter<T>(string parameterName)
        {
            var value = GetParameter(parameterName);
            
            if (value is T typedValue)
                return typedValue;
                
            try
            {
                return (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
                throw new InvalidCastException(
                    $"パラメータ '{parameterName}' を型 {typeof(T).Name} に変換できません", ex);
            }
        }
        
        /// <summary>
        /// すべてのパラメータを取得します
        /// </summary>
        /// <returns>パラメータのディクショナリ</returns>
        public virtual IDictionary<string, object> GetParameters()
        {
            return new Dictionary<string, object>(_parameters);
        }
        
        /// <summary>
        /// 画像フォーマットがサポートされているかを確認します
        /// </summary>
        /// <param name="format">画像フォーマット</param>
        /// <returns>サポートされている場合はtrue</returns>
        public virtual bool SupportsFormat(ImageFormat format)
        {
            // デフォルトではすべてのフォーマットをサポート
            // 特定のフィルターでは、特定のフォーマットのみをサポートする場合にオーバーライド
            return true;
        }
        
        /// <summary>
        /// フィルターを適用します
        /// </summary>
        /// <param name="image">入力画像</param>
        /// <returns>処理結果画像</returns>
        public abstract Task<IAdvancedImage> ApplyAsync(IAdvancedImage image);
        
        /// <summary>
        /// ステップを実行します
        /// </summary>
        /// <param name="input">入力画像</param>
        /// <param name="context">パイプライン実行コンテキスト</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>処理結果画像</returns>
        public virtual async Task<IAdvancedImage> ExecuteAsync(
            IAdvancedImage input, 
            PipelineContext context, 
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(context);
                
            cancellationToken.ThrowIfCancellationRequested();
            
            try
            {
                // フィルター適用
                return await ApplyAsync(input).ConfigureAwait(false);
            }
            catch (Exception ex) when (ErrorHandlingStrategy == StepErrorHandlingStrategy.ContinueExecution)
            {
                // エラー記録
                // 固定メッセージテンプレートを使用
                _logger?.LogError(ex, "フィルター '{FilterName}' の適用中にエラーが発生しましたが、継続実行戦略に従って処理を続行します", Name);
                
                // 元の画像を返す
                return input;
            }
            // その他の例外は上位に伝播
        }
        
        /// <summary>
        /// 出力画像情報を取得します
        /// </summary>
        /// <param name="input">入力画像</param>
        /// <returns>出力画像の情報</returns>
        public virtual PipelineImageInfo GetOutputImageInfo(IAdvancedImage input)
        {
            ArgumentNullException.ThrowIfNull(input);
            
            // 基本的には入力と同じ情報を返す
            // 特定のフィルターでは、サイズやチャンネル数が変わる場合にオーバーライドする
            var imageInfo = ((IImageFilter)this).GetOutputImageInfo(input);
            return PipelineImageInfo.FromImageInfo(imageInfo, PipelineStage.Processing);
        }

    /// <summary>
    /// フィルター適用後の画像情報を取得します
    /// </summary>
    /// <param name="inputImage">入力画像</param>
    /// <returns>出力画像の情報</returns>
    Baketa.Core.Abstractions.Imaging.ImageInfo IImageFilter.GetOutputImageInfo(IAdvancedImage inputImage)
    {
        ArgumentNullException.ThrowIfNull(inputImage);
                
        // 基本的には入力と同じ情報を返す
        return Baketa.Core.Abstractions.Imaging.ImageInfo.FromImage(inputImage);
    }
        
        /// <summary>
        /// ログを出力します
        /// </summary>
        /// <param name="logLevel">ログレベル</param>
        /// <param name="messageTemplate">メッセージテンプレート</param>
        /// <param name="args">引数</param>
        protected void Log(LogLevel logLevel, string messageTemplate, params object[] args)
        {
            if (_logger == null)
                return;
                
            _logger.Log(logLevel, messageTemplate, args);
        }
        
        /// <summary>
        /// フォーマット済みのメッセージでログを出力します
        /// </summary>
        /// <param name="logLevel">ログレベル</param>
        /// <param name="formattedMessage">フォーマット済みメッセージ</param>
        protected void LogFormatted(LogLevel logLevel, string formattedMessage)
        {
            _logger?.Log(logLevel, "{Message}", formattedMessage);
        }
    }
