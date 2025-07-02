using System;

namespace Baketa.Infrastructure.Platform.Windows.OpenCv.Exceptions;

    /// <summary>
    /// OCR処理中に発生した例外を表します
    /// </summary>
    public class OcrProcessingException : Exception
    {
        /// <summary>
        /// 新しいOcrProcessingExceptionインスタンスを初期化します
        /// </summary>
        public OcrProcessingException() : base("") { }

        /// <summary>
        /// 指定されたエラーメッセージを使用して、新しいOcrProcessingExceptionインスタンスを初期化します
        /// </summary>
        /// <param name="message">エラーメッセージ</param>
        public OcrProcessingException(string message) : base(message) { }

        /// <summary>
        /// 指定されたエラーメッセージと内部例外を使用して、新しいOcrProcessingExceptionインスタンスを初期化します
        /// </summary>
        /// <param name="message">エラーメッセージ</param>
        /// <param name="innerException">内部例外</param>
        public OcrProcessingException(string message, Exception innerException) : base(message, innerException) { }
    }
