# ローカル翻訳モデルのインターフェース設計

Baketaプロジェクトでは、複数のローカル翻訳モデル形式をサポートするための抽象化レイヤーを設計しています。このドキュメントでは、ローカル翻訳モデルの管理と使用のためのインターフェース設計について説明します。

## 1. 翻訳モデル基本インターフェース

```csharp
namespace Baketa.Core.Translation.Models
{
    /// <summary>
    /// 翻訳モデルの基本インターフェース
    /// </summary>
    public interface ITranslationModel : IDisposable
    {
        /// <summary>
        /// モデルの識別子
        /// </summary>
        string ModelId { get; }
        
        /// <summary>
        /// モデル名
        /// </summary>
        string Name { get; }
        
        /// <summary>
        /// モデルのバージョン
        /// </summary>
        string Version { get; }
        
        /// <summary>
        /// モデルのプロバイダー
        /// </summary>
        string Provider { get; }
        
        /// <summary>
        /// サポートされている翻訳元言語
        /// </summary>
        IReadOnlyList<string> SupportedSourceLanguages { get; }
        
        /// <summary>
        /// サポートされている翻訳先言語
        /// </summary>
        IReadOnlyList<string> SupportedTargetLanguages { get; }
        
        /// <summary>
        /// モデルが初期化されているかどうか
        /// </summary>
        bool IsInitialized { get; }
        
        /// <summary>
        /// モデルを初期化
        /// </summary>
        /// <returns>初期化が成功したかどうか</returns>
        Task<bool> InitializeAsync();
        
        /// <summary>
        /// 言語ペアがサポートされているかを確認
        /// </summary>
        /// <param name="sourceLanguage">翻訳元言語</param>
        /// <param name="targetLanguage">翻訳先言語</param>
        /// <returns>サポートされている場合はtrue</returns>
        bool SupportsLanguagePair(string sourceLanguage, string targetLanguage);
    }
    
    /// <summary>
    /// ローカル翻訳モデルの共通インターフェース
    /// </summary>
    public interface ILocalTranslationModel : ITranslationModel
    {
        /// <summary>
        /// モデルファイルのパス
        /// </summary>
        string ModelPath { get; }
        
        /// <summary>
        /// 使用中のデバイス
        /// </summary>
        ComputeDevice ComputeDevice { get; }
        
        /// <summary>
        /// 使用中のデバイスの種類
        /// </summary>
        ComputeDeviceType DeviceType { get; }
        
        /// <summary>
        /// モデルのメタデータ
        /// </summary>
        IReadOnlyDictionary<string, string> Metadata { get; }
        
        /// <summary>
        /// モデルのメモリ使用量（バイト単位）
        /// </summary>
        long MemoryUsage { get; }
        
        /// <summary>
        /// モデルファイルのサイズ（バイト単位）
        /// </summary>
        long ModelSize { get; }
        
        /// <summary>
        /// ハードウェアアクセラレーションをサポートしているかどうか
        /// </summary>
        bool SupportsHardwareAcceleration { get; }
        
        /// <summary>
        /// モデルをシステムのデフォルトデバイスにロード
        /// </summary>
        /// <returns>ロードが成功したかどうか</returns>
        Task<bool> LoadToDefaultDeviceAsync();
        
        /// <summary>
        /// モデルを指定されたデバイスにロード
        /// </summary>
        /// <param name="device">使用するコンピュートデバイス</param>
        /// <returns>ロードが成功したかどうか</returns>
        Task<bool> LoadToDeviceAsync(ComputeDevice device);
        
        /// <summary>
        /// モデルをアンロード
        /// </summary>
        /// <returns>アンロードが成功したかどうか</returns>
        Task<bool> UnloadAsync();
        
        /// <summary>
        /// バッチ翻訳をサポートしているかどうか
        /// </summary>
        bool SupportsBatchTranslation { get; }
        
        /// <summary>
        /// 翻訳リクエスト
        /// </summary>
        /// <param name="sourceText">翻訳元テキスト</param>
        /// <param name="sourceLanguage">翻訳元言語</param>
        /// <param name="targetLanguage">翻訳先言語</param>
        /// <param name="options">翻訳オプション</param>
        /// <returns>翻訳結果</returns>
        Task<IModelTranslationResult> TranslateAsync(
            string sourceText, 
            string sourceLanguage, 
            string targetLanguage,
            ModelTranslationOptions options = null);
        
        /// <summary>
        /// バッチ翻訳リクエスト
        /// </summary>
        /// <param name="texts">翻訳元テキストの配列</param>
        /// <param name="sourceLanguage">翻訳元言語</param>
        /// <param name="targetLanguage">翻訳先言語</param>
        /// <param name="options">翻訳オプション</param>
        /// <returns>翻訳結果の配列</returns>
        Task<IModelTranslationResult[]> TranslateBatchAsync(
            string[] texts, 
            string sourceLanguage, 
            string targetLanguage,
            ModelTranslationOptions options = null);
    }
}
```

## 2. コンピュートデバイス関連インターフェース

```csharp
namespace Baketa.Core.Translation.Models
{
    /// <summary>
    /// コンピュートデバイスの種類
    /// </summary>
    public enum ComputeDeviceType
    {
        /// <summary>
        /// CPU
        /// </summary>
        Cpu,
        
        /// <summary>
        /// CUDA対応GPU
        /// </summary>
        Cuda,
        
        /// <summary>
        /// DirectML対応GPU
        /// </summary>
        DirectML,
        
        /// <summary>
        /// OpenVINO対応ハードウェア
        /// </summary>
        OpenVINO,
        
        /// <summary>
        /// その他のハードウェアアクセラレーター
        /// </summary>
        Other
    }
    
    /// <summary>
    /// コンピュートデバイス情報
    /// </summary>
    public interface IComputeDevice
    {
        /// <summary>
        /// デバイスID
        /// </summary>
        string DeviceId { get; }
        
        /// <summary>
        /// デバイス名
        /// </summary>
        string Name { get; }
        
        /// <summary>
        /// デバイスの種類
        /// </summary>
        ComputeDeviceType DeviceType { get; }
        
        /// <summary>
        /// デバイスのメモリ容量（バイト単位）
        /// </summary>
        long MemoryCapacity { get; }
        
        /// <summary>
        /// デバイスがシステムのデフォルトかどうか
        /// </summary>
        bool IsDefault { get; }
        
        /// <summary>
        /// デバイスが利用可能かどうか
        /// </summary>
        bool IsAvailable { get; }
        
        /// <summary>
        /// デバイスの詳細情報
        /// </summary>
        IReadOnlyDictionary<string, string> Properties { get; }
    }
    
    /// <summary>
    /// コンピュートデバイス
    /// </summary>
    public class ComputeDevice : IComputeDevice
    {
        /// <inheritdoc/>
        public string DeviceId { get; }
        
        /// <inheritdoc/>
        public string Name { get; }
        
        /// <inheritdoc/>
        public ComputeDeviceType DeviceType { get; }
        
        /// <inheritdoc/>
        public long MemoryCapacity { get; }
        
        /// <inheritdoc/>
        public bool IsDefault { get; }
        
        /// <inheritdoc/>
        public bool IsAvailable { get; }
        
        /// <inheritdoc/>
        public IReadOnlyDictionary<string, string> Properties { get; }
        
        /// <summary>
        /// コンストラクタ
        /// </summary>
        public ComputeDevice(
            string deviceId,
            string name,
            ComputeDeviceType deviceType,
            long memoryCapacity,
            bool isDefault,
            bool isAvailable,
            IReadOnlyDictionary<string, string> properties = null)
        {
            DeviceId = deviceId;
            Name = name;
            DeviceType = deviceType;
            MemoryCapacity = memoryCapacity;
            IsDefault = isDefault;
            IsAvailable = isAvailable;
            Properties = properties ?? new Dictionary<string, string>();
        }
    }
}
```

## 3. コンピュートデバイスマネージャー

```csharp
namespace Baketa.Core.Translation.Models
{
    /// <summary>
    /// コンピュートデバイスマネージャーのインターフェース
    /// </summary>
    public interface IComputeDeviceManager
    {
        /// <summary>
        /// 利用可能なすべてのデバイスを取得
        /// </summary>
        /// <returns>利用可能なデバイスのコレクション</returns>
        Task<IReadOnlyList<IComputeDevice>> GetAvailableDevicesAsync();
        
        /// <summary>
        /// デフォルトのコンピュートデバイスを取得
        /// </summary>
        /// <returns>デフォルトデバイス</returns>
        Task<IComputeDevice> GetDefaultDeviceAsync();
        
        /// <summary>
        /// 指定されたタイプのデフォルトデバイスを取得
        /// </summary>
        /// <param name="deviceType">デバイスタイプ</param>
        /// <returns>指定タイプのデフォルトデバイス、見つからない場合はnull</returns>
        Task<IComputeDevice> GetDefaultDeviceOfTypeAsync(ComputeDeviceType deviceType);
        
        /// <summary>
        /// 指定されたIDのデバイスを取得
        /// </summary>
        /// <param name="deviceId">デバイスID</param>
        /// <returns>指定されたIDのデバイス、見つからない場合はnull</returns>
        Task<IComputeDevice> GetDeviceByIdAsync(string deviceId);
        
        /// <summary>
        /// 指定されたタイプのすべてのデバイスを取得
        /// </summary>
        /// <param name="deviceType">デバイスタイプ</param>
        /// <returns>指定タイプのデバイスのコレクション</returns>
        Task<IReadOnlyList<IComputeDevice>> GetDevicesByTypeAsync(ComputeDeviceType deviceType);
        
        /// <summary>
        /// 指定されたモデルを実行できるデバイスを取得
        /// </summary>
        /// <param name="modelType">モデルタイプ</param>
        /// <returns>モデルをサポートするデバイスのコレクション</returns>
        Task<IReadOnlyList<IComputeDevice>> GetCompatibleDevicesForModelAsync(string modelType);
        
        /// <summary>
        /// 指定されたメモリ要件を満たすデバイスを取得
        /// </summary>
        /// <param name="requiredMemory">必要なメモリ容量（バイト単位）</param>
        /// <returns>メモリ要件を満たすデバイスのコレクション</returns>
        Task<IReadOnlyList<IComputeDevice>> GetDevicesWithSufficientMemoryAsync(long requiredMemory);
    }
}
```

## 4. モデルリポジトリとモデル管理

```csharp
namespace Baketa.Core.Translation.Models
{
    /// <summary>
    /// 翻訳モデルリポジトリのインターフェース
    /// </summary>
    public interface ITranslationModelRepository
    {
        /// <summary>
        /// 利用可能なすべてのモデルを取得
        /// </summary>
        /// <returns>利用可能なモデルのコレクション</returns>
        Task<IReadOnlyList<ITranslationModelInfo>> GetAvailableModelsAsync();
        
        /// <summary>
        /// 指定されたIDのモデル情報を取得
        /// </summary>
        /// <param name="modelId">モデルID</param>
        /// <returns>モデル情報、見つからない場合はnull</returns>
        Task<ITranslationModelInfo> GetModelInfoByIdAsync(string modelId);
        
        /// <summary>
        /// 指定された言語ペアをサポートするモデルを取得
        /// </summary>
        /// <param name="sourceLanguage">翻訳元言語</param>
        /// <param name="targetLanguage">翻訳先言語</param>
        /// <returns>言語ペアをサポートするモデルのコレクション</returns>
        Task<IReadOnlyList<ITranslationModelInfo>> GetModelsSupportingLanguagePairAsync(
            string sourceLanguage, string targetLanguage);
        
        /// <summary>
        /// 指定されたタイプのモデルを取得
        /// </summary>
        /// <param name="modelType">モデルタイプ</param>
        /// <returns>指定タイプのモデルのコレクション</returns>
        Task<IReadOnlyList<ITranslationModelInfo>> GetModelsByTypeAsync(string modelType);
        
        /// <summary>
        /// モデル情報を保存または更新
        /// </summary>
        /// <param name="modelInfo">モデル情報</param>
        /// <returns>保存が成功したかどうか</returns>
        Task<bool> SaveModelInfoAsync(ITranslationModelInfo modelInfo);
        
        /// <summary>
        /// モデル情報を削除
        /// </summary>
        /// <param name="modelId">モデルID</param>
        /// <returns>削除が成功したかどうか</returns>
        Task<bool> DeleteModelInfoAsync(string modelId);
        
        /// <summary>
        /// モデルファイルをインポート
        /// </summary>
        /// <param name="sourcePath">ソースファイルパス</param>
        /// <param name="modelType">モデルタイプ</param>
        /// <param name="metadata">追加のメタデータ</param>
        /// <returns>インポートされたモデル情報</returns>
        Task<ITranslationModelInfo> ImportModelAsync(
            string sourcePath, 
            string modelType, 
            Dictionary<string, string> metadata = null);
    }
    
    /// <summary>
    /// 翻訳モデル情報のインターフェース
    /// </summary>
    public interface ITranslationModelInfo
    {
        /// <summary>
        /// モデルID
        /// </summary>
        string ModelId { get; }
        
        /// <summary>
        /// モデル名
        /// </summary>
        string Name { get; }
        
        /// <summary>
        /// モデルのバージョン
        /// </summary>
        string Version { get; }
        
        /// <summary>
        /// モデルタイプ
        /// </summary>
        string ModelType { get; }
        
        /// <summary>
        /// モデルファイルのパス
        /// </summary>
        string ModelPath { get; }
        
        /// <summary>
        /// 関連ファイルのパス（語彙ファイルなど）
        /// </summary>
        Dictionary<string, string> RelatedFiles { get; }
        
        /// <summary>
        /// サポートされている翻訳元言語
        /// </summary>
        IReadOnlyList<string> SupportedSourceLanguages { get; }
        
        /// <summary>
        /// サポートされている翻訳先言語
        /// </summary>
        IReadOnlyList<string> SupportedTargetLanguages { get; }
        
        /// <summary>
        /// 最低限必要なメモリ（バイト単位）
        /// </summary>
        long MinimumMemoryRequirement { get; }
        
        /// <summary>
        /// 最適なメモリ（バイト単位）
        /// </summary>
        long OptimalMemoryRequirement { get; }
        
        /// <summary>
        /// GPUアクセラレーションをサポートしているかどうか
        /// </summary>
        bool SupportsGpuAcceleration { get; }
        
        /// <summary>
        /// モデルのメタデータ
        /// </summary>
        IReadOnlyDictionary<string, string> Metadata { get; }
        
        /// <summary>
        /// モデルのサイズ（バイト単位）
        /// </summary>
        long Size { get; }
        
        /// <summary>
        /// インポート日時
        /// </summary>
        DateTimeOffset ImportedAt { get; }
        
        /// <summary>
        /// 最終使用日時
        /// </summary>
        DateTimeOffset? LastUsed { get; set; }
    }
}
```

## 5. モデル翻訳結果とオプション

```csharp
namespace Baketa.Core.Translation.Models
{
    /// <summary>
    /// モデル翻訳結果のインターフェース
    /// </summary>
    public interface IModelTranslationResult
    {
        /// <summary>
        /// 翻訳元テキスト
        /// </summary>
        string SourceText { get; }
        
        /// <summary>
        /// 翻訳結果テキスト
        /// </summary>
        string TranslatedText { get; }
        
        /// <summary>
        /// 翻訳元言語
        /// </summary>
        string SourceLanguage { get; }
        
        /// <summary>
        /// 翻訳先言語
        /// </summary>
        string TargetLanguage { get; }
        
        /// <summary>
        /// 信頼度スコア（0.0～1.0）
        /// </summary>
        float ConfidenceScore { get; }
        
        /// <summary>
        /// 処理時間（ミリ秒）
        /// </summary>
        long ProcessingTimeMs { get; }
        
        /// <summary>
        /// 使用されたモデルID
        /// </summary>
        string ModelId { get; }
        
        /// <summary>
        /// 使用されたモデル名
        /// </summary>
        string ModelName { get; }
        
        /// <summary>
        /// トークン化されたソーステキスト
        /// </summary>
        int[] SourceTokens { get; }
        
        /// <summary>
        /// 翻訳結果のトークン
        /// </summary>
        int[] OutputTokens { get; }
        
        /// <summary>
        /// 代替翻訳候補
        /// </summary>
        IReadOnlyList<ITranslationAlternative> Alternatives { get; }
        
        /// <summary>
        /// 追加のメタデータ
        /// </summary>
        IReadOnlyDictionary<string, string> Metadata { get; }
    }
    
    /// <summary>
    /// 翻訳の代替候補
    /// </summary>
    public interface ITranslationAlternative
    {
        /// <summary>
        /// 代替翻訳テキスト
        /// </summary>
        string Text { get; }
        
        /// <summary>
        /// 信頼度スコア（0.0～1.0）
        /// </summary>
        float ConfidenceScore { get; }
    }
    
    /// <summary>
    /// モデル翻訳オプション
    /// </summary>
    public class ModelTranslationOptions
    {
        /// <summary>
        /// 最大シーケンス長
        /// </summary>
        public int MaxSequenceLength { get; set; } = 512;
        
        /// <summary>
        /// 生成する最大トークン数
        /// </summary>
        public int MaxOutputTokens { get; set; } = 512;
        
        /// <summary>
        /// ビームサイズ（ビーム検索用）
        /// </summary>
        public int BeamSize { get; set; } = 4;
        
        /// <summary>
        /// 温度パラメータ
        /// </summary>
        public float Temperature { get; set; } = 1.0f;
        
        /// <summary>
        /// トップKサンプリング
        /// </summary>
        public int TopK { get; set; } = 0;
        
        /// <summary>
        /// トップPサンプリング
        /// </summary>
        public float TopP { get; set; } = 0.95f;
        
        /// <summary>
        /// 繰り返しペナルティ
        /// </summary>
        public float RepetitionPenalty { get; set; } = 1.0f;
        
        /// <summary>
        /// 長さペナルティ
        /// </summary>
        public float LengthPenalty { get; set; } = 1.0f;
        
        /// <summary>
        /// 生成する代替翻訳の数
        /// </summary>
        public int NumAlternatives { get; set; } = 0;
        
        /// <summary>
        /// ポストプロセッシングを適用するかどうか
        /// </summary>
        public bool ApplyPostProcessing { get; set; } = true;
        
        /// <summary>
        /// 自動検出された言語を使用するかどうか
        /// </summary>
        public bool UseAutoDetectedLanguage { get; set; } = false;
        
        /// <summary>
        /// コンテキスト情報
        /// </summary>
        public ITranslationContext Context { get; set; }
        
        /// <summary>
        /// 追加のモデル固有オプション
        /// </summary>
        public Dictionary<string, object> ModelSpecificOptions { get; set; } = new Dictionary<string, object>();
    }
}
```

## 6. モデルマネージャーインターフェース

```csharp
namespace Baketa.Core.Translation.Models
{
    /// <summary>
    /// 翻訳モデルマネージャーのインターフェース
    /// </summary>
    public interface ITranslationModelManager
    {
        /// <summary>
        /// 利用可能なすべてのモデルを取得
        /// </summary>
        /// <returns>利用可能なモデルのコレクション</returns>
        Task<IReadOnlyList<ITranslationModelInfo>> GetAvailableModelsAsync();
        
        /// <summary>
        /// モデルをロード
        /// </summary>
        /// <param name="modelId">モデルID</param>
        /// <returns>ロードされたモデル</returns>
        Task<ILocalTranslationModel> LoadModelAsync(string modelId);
        
        /// <summary>
        /// モデルをデバイスにロード
        /// </summary>
        /// <param name="modelId">モデルID</param>
        /// <param name="deviceId">デバイスID</param>
        /// <returns>ロードされたモデル</returns>
        Task<ILocalTranslationModel> LoadModelToDeviceAsync(string modelId, string deviceId);
        
        /// <summary>
        /// 現在ロードされているすべてのモデルを取得
        /// </summary>
        /// <returns>ロードされているモデルのコレクション</returns>
        Task<IReadOnlyList<ILocalTranslationModel>> GetLoadedModelsAsync();
        
        /// <summary>
        /// モデルをアンロード
        /// </summary>
        /// <param name="modelId">モデルID</param>
        /// <returns>アンロードが成功したかどうか</returns>
        Task<bool> UnloadModelAsync(string modelId);
        
        /// <summary>
        /// すべてのモデルをアンロード
        /// </summary>
        /// <returns>アンロードが成功したかどうか</returns>
        Task<bool> UnloadAllModelsAsync();
        
        /// <summary>
        /// 指定された言語ペアに最適なモデルを取得
        /// </summary>
        /// <param name="sourceLanguage">翻訳元言語</param>
        /// <param name="targetLanguage">翻訳先言語</param>
        /// <returns>最適なモデル、見つからない場合はnull</returns>
        Task<ILocalTranslationModel> GetBestModelForLanguagePairAsync(
            string sourceLanguage, string targetLanguage);
        
        /// <summary>
        /// モデルを追加
        /// </summary>
        /// <param name="modelPath">モデルファイルのパス</param>
        /// <param name="modelType">モデルタイプ</param>
        /// <param name="metadata">追加のメタデータ</param>
        /// <returns>追加されたモデル情報</returns>
        Task<ITranslationModelInfo> AddModelAsync(
            string modelPath, 
            string modelType, 
            Dictionary<string, string> metadata = null);
        
        /// <summary>
        /// モデルを削除
        /// </summary>
        /// <param name="modelId">モデルID</param>
        /// <param name="removeFiles">ファイルも削除するかどうか</param>
        /// <returns>削除が成功したかどうか</returns>
        Task<bool> RemoveModelAsync(string modelId, bool removeFiles = false);
        
        /// <summary>
        /// 未使用のモデルをクリーンアップ
        /// </summary>
        /// <param name="olderThan">指定された日時より古いモデルをクリーンアップ</param>
        /// <returns>クリーンアップされたモデルの数</returns>
        Task<int> CleanupUnusedModelsAsync(DateTimeOffset olderThan);
    }
}
```

## 7. ファクトリーインターフェース

```csharp
namespace Baketa.Core.Translation.Models
{
    /// <summary>
    /// 翻訳モデルファクトリーのインターフェース
    /// </summary>
    public interface ITranslationModelFactory
    {
        /// <summary>
        /// モデル情報からモデルを作成
        /// </summary>
        /// <param name="modelInfo">モデル情報</param>
        /// <returns>モデルインスタンス</returns>
        Task<ILocalTranslationModel> CreateModelAsync(ITranslationModelInfo modelInfo);
        
        /// <summary>
        /// モデルを作成
        /// </summary>
        /// <param name="modelPath">モデルファイルのパス</param>
        /// <param name="modelType">モデルタイプ</param>
        /// <param name="options">モデル作成オプション</param>
        /// <returns>モデルインスタンス</returns>
        Task<ILocalTranslationModel> CreateModelAsync(
            string modelPath, 
            string modelType, 
            ModelCreationOptions options = null);
        
        /// <summary>
        /// モデルメタデータを読み取り
        /// </summary>
        /// <param name="modelPath">モデルファイルのパス</param>
        /// <param name="modelType">モデルタイプ</param>
        /// <returns>モデルメタデータ</returns>
        Task<IReadOnlyDictionary<string, string>> ReadModelMetadataAsync(
            string modelPath, string modelType);
        
        /// <summary>
        /// サポートされているモデルタイプを取得
        /// </summary>
        /// <returns>サポートされているモデルタイプのコレクション</returns>
        IReadOnlyList<string> GetSupportedModelTypes();
        
        /// <summary>
        /// 指定されたタイプのモデルがサポートされているかを確認
        /// </summary>
        /// <param name="modelType">モデルタイプ</param>
        /// <returns>サポートされている場合はtrue</returns>
        bool SupportsModelType(string modelType);
    }
    
    /// <summary>
    /// モデル作成オプション
    /// </summary>
    public class ModelCreationOptions
    {
        /// <summary>
        /// モデル名
        /// </summary>
        public string Name { get; set; }
        
        /// <summary>
        /// モデルID（指定しない場合は自動生成）
        /// </summary>
        public string ModelId { get; set; }
        
        /// <summary>
        /// 関連ファイルのパス（語彙ファイルなど）
        /// </summary>
        public Dictionary<string, string> RelatedFiles { get; set; } = new Dictionary<string, string>();
        
        /// <summary>
        /// サポートされている翻訳元言語
        /// </summary>
        public List<string> SupportedSourceLanguages { get; set; }
        
        /// <summary>
        /// サポートされている翻訳先言語
        /// </summary>
        public List<string> SupportedTargetLanguages { get; set; }
        
        /// <summary>
        /// ロード時に使用するデバイスタイプ
        /// </summary>
        public ComputeDeviceType? PreferredDeviceType { get; set; }
        
        /// <summary>
        /// 追加のメタデータ
        /// </summary>
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
        
        /// <summary>
        /// モデル固有のオプション
        /// </summary>
        public Dictionary<string, object> ModelSpecificOptions { get; set; } = new Dictionary<string, object>();
    }
}
```