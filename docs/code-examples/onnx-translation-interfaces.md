# ONNX翻訳のインターフェース設計

Baketaプロジェクトでは、ONNX (Open Neural Network Exchange) フォーマットを使用したローカル翻訳モデルをサポートしています。このドキュメントでは、ONNX翻訳エンジンのインターフェース設計について説明します。

## 1. 基本抽象化インターフェース

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
}
```

## 2. ONNX専用インターフェース

```csharp
namespace Baketa.Core.Translation.Models.Onnx
{
    /// <summary>
    /// ONNXモデル特有の操作を定義するインターフェース
    /// </summary>
    public interface IOnnxTranslationModel : ITranslationModel
    {
        /// <summary>
        /// ONNXモデルのパス
        /// </summary>
        string ModelPath { get; }
        
        /// <summary>
        /// モデルに関連する語彙ファイルのパス
        /// </summary>
        string VocabularyPath { get; }
        
        /// <summary>
        /// 使用中のONNXセッション
        /// </summary>
        IOnnxSession Session { get; }
        
        /// <summary>
        /// ONNXモデルの実行時オプション
        /// </summary>
        IOnnxSessionOptions SessionOptions { get; }
        
        /// <summary>
        /// モデルのメタデータ
        /// </summary>
        IReadOnlyDictionary<string, string> Metadata { get; }
        
        /// <summary>
        /// モデルの入力ノード名
        /// </summary>
        IReadOnlyList<string> InputNames { get; }
        
        /// <summary>
        /// モデルの出力ノード名
        /// </summary>
        IReadOnlyList<string> OutputNames { get; }
        
        /// <summary>
        /// モデルに関連するトークナイザー
        /// </summary>
        ITokenizer Tokenizer { get; }
        
        /// <summary>
        /// GPUが使用可能かどうかを確認
        /// </summary>
        /// <returns>GPUが使用可能な場合はtrue</returns>
        bool IsGpuAvailable();
        
        /// <summary>
        /// 使用中の実行プロバイダー
        /// </summary>
        OnnxExecutionProvider CurrentExecutionProvider { get; }
        
        /// <summary>
        /// 実行プロバイダーを変更
        /// </summary>
        /// <param name="provider">使用する実行プロバイダー</param>
        /// <returns>変更が成功したかどうか</returns>
        Task<bool> SetExecutionProviderAsync(OnnxExecutionProvider provider);
    }
    
    /// <summary>
    /// ONNX実行プロバイダー
    /// </summary>
    public enum OnnxExecutionProvider
    {
        /// <summary>
        /// CPU上で実行
        /// </summary>
        Cpu,
        
        /// <summary>
        /// CUDA GPUで実行
        /// </summary>
        Cuda,
        
        /// <summary>
        /// DirectML GPUで実行
        /// </summary>
        DirectML,
        
        /// <summary>
        /// OpenVINOで実行
        /// </summary>
        OpenVINO
    }
    
    /// <summary>
    /// ONNXセッション抽象化インターフェース
    /// </summary>
    public interface IOnnxSession : IDisposable
    {
        /// <summary>
        /// 推論の実行
        /// </summary>
        /// <param name="inputs">入力テンソルのコレクション</param>
        /// <returns>推論結果のディクショナリ</returns>
        Task<IDictionary<string, IOnnxTensor>> RunAsync(IDictionary<string, IOnnxTensor> inputs);
        
        /// <summary>
        /// 推論の実行
        /// </summary>
        /// <param name="inputNames">入力ノード名</param>
        /// <param name="inputs">入力テンソル</param>
        /// <param name="outputNames">出力ノード名</param>
        /// <returns>推論結果の配列</returns>
        Task<IOnnxTensor[]> RunAsync(string[] inputNames, IOnnxTensor[] inputs, string[] outputNames);
    }
    
    /// <summary>
    /// ONNXテンソル抽象化インターフェース
    /// </summary>
    public interface IOnnxTensor : IDisposable
    {
        /// <summary>
        /// テンソルの次元情報
        /// </summary>
        long[] Dimensions { get; }
        
        /// <summary>
        /// テンソルの要素数
        /// </summary>
        long ElementCount { get; }
        
        /// <summary>
        /// テンソルのデータ型
        /// </summary>
        Type ElementType { get; }
        
        /// <summary>
        /// テンソルデータの取得
        /// </summary>
        /// <typeparam name="T">データ型</typeparam>
        /// <returns>テンソルデータ</returns>
        T[] GetData<T>();
    }
}
```

## 3. トークナイザーインターフェース

```csharp
namespace Baketa.Core.Translation.Models.Tokenization
{
    /// <summary>
    /// テキストトークン化のインターフェース
    /// </summary>
    public interface ITokenizer
    {
        /// <summary>
        /// トークナイザーの識別子
        /// </summary>
        string TokenizerId { get; }
        
        /// <summary>
        /// トークナイザー名
        /// </summary>
        string Name { get; }
        
        /// <summary>
        /// 語彙サイズ
        /// </summary>
        int VocabularySize { get; }
        
        /// <summary>
        /// テキストをトークン化
        /// </summary>
        /// <param name="text">入力テキスト</param>
        /// <returns>トークン配列</returns>
        int[] Tokenize(string text);
        
        /// <summary>
        /// トークンをテキストに変換
        /// </summary>
        /// <param name="tokens">トークン配列</param>
        /// <returns>デコードされたテキスト</returns>
        string Decode(int[] tokens);
        
        /// <summary>
        /// トークンをテキストに変換
        /// </summary>
        /// <param name="token">単一トークン</param>
        /// <returns>デコードされたテキスト</returns>
        string DecodeToken(int token);
        
        /// <summary>
        /// 特殊トークン
        /// </summary>
        IReadOnlyDictionary<string, int> SpecialTokens { get; }
    }
    
    /// <summary>
    /// 言語固有のトークナイザー
    /// </summary>
    public interface ILanguageTokenizer : ITokenizer
    {
        /// <summary>
        /// サポートされている言語
        /// </summary>
        IReadOnlyList<string> SupportedLanguages { get; }
        
        /// <summary>
        /// 言語に固有のトークン化
        /// </summary>
        /// <param name="text">入力テキスト</param>
        /// <param name="language">言語コード</param>
        /// <returns>トークン配列</returns>
        int[] TokenizeForLanguage(string text, string language);
    }
}
```

## 4. ONNX翻訳モデルファクトリー

```csharp
namespace Baketa.Core.Translation.Models.Onnx
{
    /// <summary>
    /// ONNXモデルのファクトリーインターフェース
    /// </summary>
    public interface IOnnxModelFactory
    {
        /// <summary>
        /// 指定されたパスからONNXモデルを作成
        /// </summary>
        /// <param name="modelPath">モデルファイルのパス</param>
        /// <param name="vocabularyPath">語彙ファイルのパス</param>
        /// <param name="options">モデル作成オプション</param>
        /// <returns>ONNXモデルインスタンス</returns>
        Task<IOnnxTranslationModel> CreateModelAsync(string modelPath, string vocabularyPath, OnnxModelOptions options = null);
        
        /// <summary>
        /// モデルメタデータの読み取り
        /// </summary>
        /// <param name="modelPath">モデルファイルのパス</param>
        /// <returns>モデルメタデータ</returns>
        Task<IReadOnlyDictionary<string, string>> ReadModelMetadataAsync(string modelPath);
        
        /// <summary>
        /// 適切なトークナイザーの作成
        /// </summary>
        /// <param name="vocabularyPath">語彙ファイルのパス</param>
        /// <param name="modelType">モデルの種類</param>
        /// <returns>トークナイザーインスタンス</returns>
        Task<ITokenizer> CreateTokenizerAsync(string vocabularyPath, string modelType);
    }
    
    /// <summary>
    /// ONNXモデルの作成オプション
    /// </summary>
    public class OnnxModelOptions
    {
        /// <summary>
        /// 使用する実行プロバイダー
        /// </summary>
        public OnnxExecutionProvider ExecutionProvider { get; set; } = OnnxExecutionProvider.Cpu;
        
        /// <summary>
        /// グラフ最適化レベル
        /// </summary>
        public GraphOptimizationLevel OptimizationLevel { get; set; } = GraphOptimizationLevel.All;
        
        /// <summary>
        /// シーケンスの最大長
        /// </summary>
        public int MaxSequenceLength { get; set; } = 512;
        
        /// <summary>
        /// 使用するスレッド数
        /// </summary>
        public int ThreadCount { get; set; } = 0; // 0は自動
        
        /// <summary>
        /// モデルの種類
        /// </summary>
        public string ModelType { get; set; } = "transformer";
    }
    
    /// <summary>
    /// グラフ最適化レベル
    /// </summary>
    public enum GraphOptimizationLevel
    {
        /// <summary>
        /// 最適化なし
        /// </summary>
        None,
        
        /// <summary>
        /// 基本的な最適化
        /// </summary>
        Basic,
        
        /// <summary>
        /// 拡張最適化
        /// </summary>
        Extended,
        
        /// <summary>
        /// すべての最適化を適用
        /// </summary>
        All
    }
}
```