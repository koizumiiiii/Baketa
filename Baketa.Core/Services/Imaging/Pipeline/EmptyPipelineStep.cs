using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Imaging.Pipeline;

namespace Baketa.Core.Services.Imaging.Pipeline;

    /// <summary>
    /// テスト用の空のパイプラインステップ
    /// </summary>
#pragma warning disable CS8603, CS8600, CA1031, CA2263 // テスト用スタブクラスのために警告を抑制
    public class EmptyPipelineStep : IImagePipelineStep
    {
        /// <summary>
        /// ステップの名前
        /// </summary>
        public string Name => "Empty";

        /// <summary>
        /// ステップの説明
        /// </summary>
        public string Description => "空のテスト用ステップ";

        /// <summary>
        /// ステップのパラメータ定義
        /// </summary>
        public IReadOnlyCollection<PipelineStepParameter> Parameters => [];

        /// <summary>
        /// ステップのエラーハンドリング戦略
        /// </summary>
        public StepErrorHandlingStrategy ErrorHandlingStrategy { get; set; } = StepErrorHandlingStrategy.LogAndContinue;

        /// <summary>
        /// ステップを実行します
        /// </summary>
        /// <param name="input">入力画像</param>
        /// <param name="context">パイプライン実行コンテキスト</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>入力画像をそのまま返す</returns>
        public Task<IAdvancedImage> ExecuteAsync(IAdvancedImage input, PipelineContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(input);
        }

        /// <summary>
        /// パラメータ値を取得します
        /// </summary>
        /// <param name="parameterName">パラメータ名</param>
        /// <returns>デフォルト値</returns>
        public object GetParameter(string parameterName)
        {
            ArgumentNullException.ThrowIfNull(parameterName);
            // 少なくとも非nullの値を返す
            return string.Empty; // 空文字列を返すことでnullではなくなる
        }
        
        /// <summary>
        /// パラメータ値をジェネリック型で取得します
        /// </summary>
        /// <typeparam name="T">取得する型</typeparam>
        /// <param name="parameterName">パラメータ名</param>
        /// <returns>デフォルト値</returns>
        public T GetParameter<T>(string parameterName)
        {
            ArgumentNullException.ThrowIfNull(parameterName);

            // 型に応じた代替値を返す
            if (typeof(T) == typeof(string))
            {
                return (T)(object)string.Empty;
            }
            
            if (typeof(T) == typeof(int) || 
                typeof(T) == typeof(double) || 
                typeof(T) == typeof(float) || 
                typeof(T) == typeof(long) || 
                typeof(T) == typeof(bool))
            {
                return default;
            }
            
            // オブジェクト型の場合は新しいインスタンスを作成
            if (typeof(T) == typeof(object))
            {
                return (T)(object)new object();
            }
            
            // クラス型の場合はデフォルトコンストラクタを使用してインスタンス化を試みる
            Type type = typeof(T);
            if (type.IsClass && !type.IsAbstract)
            {
                try
                {
                    // デフォルトコンストラクタがある場合は出来る限りインスタンス化
                    return Activator.CreateInstance<T>();
                }
                catch (MissingMethodException)
                {
                    // デフォルトコンストラクタがない場合
                    // テスト用に例外をスローせずにデフォルト値を返す
                    return default;
                }
                catch (InvalidOperationException)
                {
                    // インスタンス化できない場合
                    return default;
                }
                catch (TargetInvocationException)
                {
                    // コンストラクタの呼び出しで例外が発生した場合
                    return default;
                }
            }
            
            // その他の場合はデフォルト値を返す
            return default;
        }
        
        /// <summary>
        /// 出力画像情報を取得します
        /// </summary>
        /// <param name="input">入力画像</param>
        /// <returns>入力と同じ画像情報</returns>
        public PipelineImageInfo GetOutputImageInfo(IAdvancedImage input)
        {
            ArgumentNullException.ThrowIfNull(input);
            
            // 入力画像から情報を作成
            var imageInfo = new Baketa.Core.Abstractions.Imaging.ImageInfo
            {
                Width = input.Width,
                Height = input.Height,
                Format = input.Format,
                Channels = input.Format == ImageFormat.Grayscale8 ? 1 : 
                           input.Format == ImageFormat.Rgb24 ? 3 : 4
            };
            
            return PipelineImageInfo.FromImageInfo(imageInfo, PipelineStage.Processing);
        }

        /// <summary>
        /// パラメータ値を設定します (何もしません)
        /// </summary>
        /// <param name="parameterName">パラメータ名</param>
        /// <param name="value">設定する値</param>
        public void SetParameter(string parameterName, object value)
        {
            // 何もしない (テスト用)
        }
    }
#pragma warning restore CS8603, CS8600, CA1031, CA2263
