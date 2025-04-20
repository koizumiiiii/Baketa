using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Baketa.Core.DI.Exceptions
{
    /// <summary>
    /// モジュール間の循環依存が検出された場合にスローされる例外。
    /// </summary>
    [Serializable]
    public class CircularDependencyException : Exception
    {
        /// <summary>
        /// 循環依存の発生したモジュールパス
        /// </summary>
        public IReadOnlyList<Type>? DependencyCycle { get; }

        /// <summary>
        /// 新しい <see cref="CircularDependencyException"/> インスタンスを初期化します。
        /// </summary>
        public CircularDependencyException() : base("モジュール間の循環依存が検出されました。")
        {
        }

        /// <summary>
        /// 指定したエラーメッセージを使用して、新しい <see cref="CircularDependencyException"/> インスタンスを初期化します。
        /// </summary>
        /// <param name="message">例外の原因を説明するメッセージ</param>
        public CircularDependencyException(string message) : base(message)
        {
        }

        /// <summary>
        /// 指定したエラーメッセージと内部例外を使用して、新しい <see cref="CircularDependencyException"/> インスタンスを初期化します。
        /// </summary>
        /// <param name="message">例外の原因を説明するメッセージ</param>
        /// <param name="innerException">現在の例外の原因である例外</param>
        public CircularDependencyException(string message, Exception innerException) : base(message, innerException)
        {
        }

        /// <summary>
        /// 指定したエラーメッセージと循環依存パスを使用して、新しい <see cref="CircularDependencyException"/> インスタンスを初期化します。
        /// </summary>
        /// <param name="message">例外の原因を説明するメッセージ</param>
        /// <param name="dependencyCycle">循環依存の発生したモジュールパス</param>
        public CircularDependencyException(string message, IReadOnlyList<Type> dependencyCycle) : base(message)
        {
            DependencyCycle = dependencyCycle;
        }

        /// <summary>
        /// シリアル化したデータを使用して <see cref="CircularDependencyException"/> クラスの新しいインスタンスを初期化します。
        /// </summary>
        /// <param name="info">例外に関するシリアル化済み情報を保持する SerializationInfo</param>
        /// <param name="context">デシリアライズの送信元または送信先に関するコンテキスト情報</param>
#pragma warning disable SYSLIB0051 // タイプまたはメンバーは旧形式です
        protected CircularDependencyException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
#pragma warning restore SYSLIB0051
    }
}