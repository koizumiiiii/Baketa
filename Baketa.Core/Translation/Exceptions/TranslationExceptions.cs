using System;
using System.Runtime.Serialization;

namespace Baketa.Core.Translation.Exceptions;

/// <summary>
/// 翻訳システムの基底例外クラス
/// </summary>
[Serializable]
public abstract class TranslationBaseException : Exception
    {
        /// <summary>
        /// エラーコード
        /// </summary>
        public string ErrorCode { get; }
        
        /// <summary>
        /// エラーの場所情報
        /// </summary>
        public string LocationInfo { get; }
        
        /// <summary>
        /// 再試行可能かどうか
        /// </summary>
        public bool IsRetryable { get; }
        
        /// <summary>
        /// 新しい翻訳基底例外を初期化します
        /// </summary>
        protected TranslationBaseException()
            : base()
        {
            ErrorCode = "UNKNOWN";
            LocationInfo = string.Empty;
            IsRetryable = false;
        }

        /// <summary>
        /// 新しい翻訳基底例外をメッセージ付きで初期化します
        /// </summary>
        protected TranslationBaseException(string message)
            : base(message)
        {
            ErrorCode = "UNKNOWN";
            LocationInfo = string.Empty;
            IsRetryable = false;
        }

        /// <summary>
        /// 新しい翻訳基底例外をメッセージと内部例外付きで初期化します
        /// </summary>
        protected TranslationBaseException(string message, Exception innerException)
            : base(message, innerException)
        {
            ErrorCode = "UNKNOWN";
            LocationInfo = string.Empty;
            IsRetryable = false;
        }
        
        /// <summary>
        /// 新しい翻訳基底例外を詳細情報付きで初期化します
        /// </summary>
        protected TranslationBaseException(
            string message, 
            string errorCode, 
            string locationInfo = "", 
            bool isRetryable = false, 
            Exception? innerException = null)
            : base(message, innerException)
        {
            ErrorCode = errorCode;
            LocationInfo = locationInfo;
            IsRetryable = isRetryable;
        }
        
        /// <summary>
        /// シリアライズ用コンストラクタ
        /// </summary>
        [Obsolete("This API supports obsolete formatter-based serialization.", DiagnosticId = "SYSLIB0051")]
        protected TranslationBaseException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            // シリアライズされたプロパティを取得
            ErrorCode = info.GetString(nameof(ErrorCode)) ?? "UNKNOWN";
            LocationInfo = info.GetString(nameof(LocationInfo)) ?? string.Empty;
            IsRetryable = info.GetBoolean(nameof(IsRetryable));
        }
        
        /// <summary>
        /// シリアライズ時にオブジェクトデータを保存
        /// </summary>
        [System.Security.SecurityCritical]
        [Obsolete("This API supports obsolete formatter-based serialization.", DiagnosticId = "SYSLIB0051")]
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            ArgumentNullException.ThrowIfNull(info, nameof(info));
                
            info.AddValue(nameof(ErrorCode), ErrorCode);
            info.AddValue(nameof(LocationInfo), LocationInfo);
            info.AddValue(nameof(IsRetryable), IsRetryable);
            
            base.GetObjectData(info, context);
        }
    }
    
    /// <summary>
    /// 翻訳データ処理に関する例外
    /// </summary>
    [Serializable]
    public class TranslationDataException : TranslationBaseException
    {
        /// <summary>
        /// 新しい翻訳データ例外を初期化します
        /// </summary>
        public TranslationDataException()
            : base()
        {
        }

        /// <summary>
        /// 新しい翻訳データ例外をメッセージ付きで初期化します
        /// </summary>
        public TranslationDataException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// 新しい翻訳データ例外をメッセージと内部例外付きで初期化します
        /// </summary>
        public TranslationDataException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// 新しい翻訳データ例外を詳細情報付きで初期化します
        /// </summary>
        public TranslationDataException(
            string message,
            string errorCode,
            string locationInfo = "",
            bool isRetryable = false,
            Exception? innerException = null)
            : base(message, errorCode, locationInfo, isRetryable, innerException)
        {
        }
        
        /// <summary>
        /// シリアライズ用コンストラクタ
        /// </summary>
        [Obsolete("This API supports obsolete formatter-based serialization.", DiagnosticId = "SYSLIB0051")]
        protected TranslationDataException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
    
    /// <summary>
    /// 翻訳ネットワーク通信に関する例外
    /// </summary>
    [Serializable]
    public class TranslationNetworkException : TranslationBaseException
    {
        /// <summary>
        /// 新しい翻訳ネットワーク例外を初期化します
        /// </summary>
        public TranslationNetworkException()
            : base()
        {
        }

        /// <summary>
        /// 新しい翻訳ネットワーク例外をメッセージ付きで初期化します
        /// </summary>
        public TranslationNetworkException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// 新しい翻訳ネットワーク例外をメッセージと内部例外付きで初期化します
        /// </summary>
        public TranslationNetworkException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// 新しい翻訳ネットワーク例外を詳細情報付きで初期化します
        /// </summary>
        public TranslationNetworkException(
            string message,
            string errorCode,
            string locationInfo = "",
            bool isRetryable = true, // ネットワークエラーは基本的に再試行可能
            Exception? innerException = null)
            : base(message, errorCode, locationInfo, isRetryable, innerException)
        {
        }
        
        /// <summary>
        /// シリアライズ用コンストラクタ
        /// </summary>
        [Obsolete("This API supports obsolete formatter-based serialization.", DiagnosticId = "SYSLIB0051")]
        protected TranslationNetworkException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
    
    /// <summary>
    /// 翻訳設定に関する例外
    /// </summary>
    [Serializable]
    public class TranslationConfigurationException : TranslationBaseException
    {
        /// <summary>
        /// 新しい翻訳設定例外を初期化します
        /// </summary>
        public TranslationConfigurationException()
            : base()
        {
        }

        /// <summary>
        /// 新しい翻訳設定例外をメッセージ付きで初期化します
        /// </summary>
        public TranslationConfigurationException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// 新しい翻訳設定例外をメッセージと内部例外付きで初期化します
        /// </summary>
        public TranslationConfigurationException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// 新しい翻訳設定例外を詳細情報付きで初期化します
        /// </summary>
        public TranslationConfigurationException(
            string message,
            string errorCode,
            string locationInfo = "",
            bool isRetryable = false, // 設定エラーは基本的に再試行不可能
            Exception? innerException = null)
            : base(message, errorCode, locationInfo, isRetryable, innerException)
        {
        }
        
        /// <summary>
        /// シリアライズ用コンストラクタ
        /// </summary>
        [Obsolete("This API supports obsolete formatter-based serialization.", DiagnosticId = "SYSLIB0051")]
        protected TranslationConfigurationException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
    
    /// <summary>
    /// 翻訳キャッシュに関する例外
    /// </summary>
    [Serializable]
    public class TranslationCacheException : TranslationDataException
    {
        /// <summary>
        /// 新しい翻訳キャッシュ例外を初期化します
        /// </summary>
        public TranslationCacheException()
            : base()
        {
        }

        /// <summary>
        /// 新しい翻訳キャッシュ例外をメッセージ付きで初期化します
        /// </summary>
        public TranslationCacheException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// 新しい翻訳キャッシュ例外をメッセージと内部例外付きで初期化します
        /// </summary>
        public TranslationCacheException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// 新しい翻訳キャッシュ例外を詳細情報付きで初期化します
        /// </summary>
        public TranslationCacheException(
            string message,
            string errorCode,
            string locationInfo = "",
            bool isRetryable = false,
            Exception? innerException = null)
            : base(message, errorCode, locationInfo, isRetryable, innerException)
        {
        }
        
        /// <summary>
        /// シリアライズ用コンストラクタ
        /// </summary>
        [Obsolete("This API supports obsolete formatter-based serialization.", DiagnosticId = "SYSLIB0051")]
        protected TranslationCacheException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
    
    /// <summary>
    /// 翻訳エンジンに関する例外
    /// </summary>
    [Serializable]
    public class TranslationEngineException : TranslationBaseException
    {
        /// <summary>
        /// 新しい翻訳エンジン例外を初期化します
        /// </summary>
        public TranslationEngineException()
            : base()
        {
        }

        /// <summary>
        /// 新しい翻訳エンジン例外をメッセージ付きで初期化します
        /// </summary>
        public TranslationEngineException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// 新しい翻訳エンジン例外をメッセージと内部例外付きで初期化します
        /// </summary>
        public TranslationEngineException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// 新しい翻訳エンジン例外を詳細情報付きで初期化します
        /// </summary>
        public TranslationEngineException(
            string message,
            string errorCode,
            string locationInfo = "",
            bool isRetryable = false,
            Exception? innerException = null)
            : base(message, errorCode, locationInfo, isRetryable, innerException)
        {
        }
        
        /// <summary>
        /// シリアライズ用コンストラクタ
        /// </summary>
        [Obsolete("This API supports obsolete formatter-based serialization.", DiagnosticId = "SYSLIB0051")]
        protected TranslationEngineException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
    
    /// <summary>
    /// 翻訳リポジトリに関する例外
    /// </summary>
    [Serializable]
    public class TranslationRepositoryException : TranslationDataException
    {
        /// <summary>
        /// 新しい翻訳リポジトリ例外を初期化します
        /// </summary>
        public TranslationRepositoryException()
            : base()
        {
        }

        /// <summary>
        /// 新しい翻訳リポジトリ例外をメッセージ付きで初期化します
        /// </summary>
        public TranslationRepositoryException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// 新しい翻訳リポジトリ例外をメッセージと内部例外付きで初期化します
        /// </summary>
        public TranslationRepositoryException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// 新しい翻訳リポジトリ例外を詳細情報付きで初期化します
        /// </summary>
        public TranslationRepositoryException(
            string message,
            string errorCode,
            string locationInfo = "",
            bool isRetryable = false,
            Exception? innerException = null)
            : base(message, errorCode, locationInfo, isRetryable, innerException)
        {
        }
        
        /// <summary>
        /// シリアライズ用コンストラクタ
        /// </summary>
        [Obsolete("This API supports obsolete formatter-based serialization.", DiagnosticId = "SYSLIB0051")]
        protected TranslationRepositoryException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
    
    /// <summary>
    /// 翻訳イベントに関する例外
    /// </summary>
    [Serializable]
    public class TranslationEventException : TranslationBaseException
    {
        /// <summary>
        /// 新しい翻訳イベント例外を初期化します
        /// </summary>
        public TranslationEventException()
            : base()
        {
        }

        /// <summary>
        /// 新しい翻訳イベント例外をメッセージ付きで初期化します
        /// </summary>
        public TranslationEventException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// 新しい翻訳イベント例外をメッセージと内部例外付きで初期化します
        /// </summary>
        public TranslationEventException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// 新しい翻訳イベント例外を詳細情報付きで初期化します
        /// </summary>
        public TranslationEventException(
            string message,
            string errorCode,
            string locationInfo = "",
            bool isRetryable = false,
            Exception? innerException = null)
            : base(message, errorCode, locationInfo, isRetryable, innerException)
        {
        }
        
        /// <summary>
        /// シリアライズ用コンストラクタ
        /// </summary>
        [Obsolete("This API supports obsolete formatter-based serialization.", DiagnosticId = "SYSLIB0051")]
        protected TranslationEventException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
