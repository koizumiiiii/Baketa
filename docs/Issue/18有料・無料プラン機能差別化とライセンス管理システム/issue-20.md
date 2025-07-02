# Issue 20: ローカル翻訳モデル最適化（無料プラン向け）

## 概要
Baketaの無料プラン向けに、ローカルで動作するHelsinki-NLP OPUS-MT ONNXモデルを統合・最適化します。インターネット接続なしでも基本的な翻訳機能を提供しつつ、リソース使用量を最小限に抑え、ゲームプレイへの影響を最小化します。

## 目的・理由
1. **オフライン翻訳の実現**: インターネット接続がなくても翻訳機能を利用可能にする
2. **リソース効率の向上**: 限られたメモリとCPUリソースで効率的に動作するよう最適化する
3. **ゲームパフォーマンスへの影響最小化**: メイン用途であるゲームプレイへの影響を抑制する
4. **基本翻訳品質の確保**: 無料プランでも実用的な翻訳品質を提供する

## 詳細
- Helsinki-NLP OPUS-MT ONNXモデルの統合
- 効率的なモデル管理とメモリ使用の最適化
- SentencePiece連携の実装
- モデルファイルの配布・管理戦略の実装

## タスク分解
- [ ] ONNXモデル実装
  - [ ] Helsinki-NLP OPUS-MT ONNXモデルの選定と評価
  - [ ] ONNX Runtimeの統合と設定
  - [ ] モデル読み込みとメモリ最適化の実装
  - [ ] 推論処理の効率化と並列処理の実装
- [ ] SentencePiece連携
  - [ ] SentencePieceProcessorの統合
  - [ ] トークナイズ・デトークナイズ処理の実装
  - [ ] 前処理・後処理パイプラインの最適化
  - [ ] 言語ペアごとのトークナイザーモデル管理
- [ ] モデルファイル管理
  - [ ] ModelManagerの実装（遅延ロード、メモリ管理）
  - [ ] モデルのオンデマンドダウンロード機能
  - [ ] バージョン管理とアップデート機能
  - [ ] キャッシュ管理と障害復旧機能
- [ ] モデル配布戦略
  - [ ] 初期インストール時の基本モデル（日英・英日）同梱
  - [ ] 他言語モデルのオンデマンドダウンロード機能
  - [ ] 不要モデル削除オプションの実装
  - [ ] ダウンロード進捗表示UI
- [ ] エラーハンドリング
  - [ ] モデルロードエラーの検出と対応
  - [ ] 推論エラーの処理（長文分割等）
  - [ ] トークン制限超過の検出と対応
  - [ ] トークナイズ/デコードエラーの処理
- [ ] パフォーマンス最適化
  - [ ] モデル量子化の検討と実装
  - [ ] 推論速度の最適化
  - [ ] メモリ使用量の最適化
  - [ ] CPU使用率の最適化
- [ ] テストと評価
  - [ ] 翻訳品質の評価
  - [ ] パフォーマンス評価
  - [ ] リソース使用量の評価
  - [ ] 長時間安定性テスト

## クラスとインターフェース設計案

```csharp
namespace Baketa.Translation.Local
{
    /// <summary>
    /// ローカル翻訳モデル管理インターフェース
    /// </summary>
    public interface ILocalModelManager
    {
        /// <summary>
        /// 利用可能なモデルのリストを取得します
        /// </summary>
        /// <returns>モデルリスト</returns>
        Task<IReadOnlyList<LocalModelInfo>> GetAvailableModelsAsync();
        
        /// <summary>
        /// モデルをロードします
        /// </summary>
        /// <param name="languagePair">言語ペア</param>
        /// <returns>ロードされたモデル</returns>
        Task<LocalTranslationModel?> LoadModelAsync(LanguagePair languagePair);
        
        /// <summary>
        /// モデルをアンロードします
        /// </summary>
        /// <param name="languagePair">言語ペア</param>
        /// <returns>アンロードが成功したかどうか</returns>
        Task<bool> UnloadModelAsync(LanguagePair languagePair);
        
        /// <summary>
        /// モデルをダウンロードします
        /// </summary>
        /// <param name="languagePair">言語ペア</param>
        /// <param name="progressCallback">進捗コールバック</param>
        /// <returns>ダウンロードが成功したかどうか</returns>
        Task<bool> DownloadModelAsync(LanguagePair languagePair, IProgress<double>? progressCallback = null);
        
        /// <summary>
        /// モデルを削除します
        /// </summary>
        /// <param name="languagePair">言語ペア</param>
        /// <returns>削除が成功したかどうか</returns>
        Task<bool> DeleteModelAsync(LanguagePair languagePair);
        
        /// <summary>
        /// モデルのアップデートをチェックします
        /// </summary>
        /// <returns>アップデートが利用可能なモデルのリスト</returns>
        Task<IReadOnlyList<LocalModelInfo>> CheckForUpdatesAsync();
        
        /// <summary>
        /// モデルをアップデートします
        /// </summary>
        /// <param name="languagePair">言語ペア</param>
        /// <param name="progressCallback">進捗コールバック</param>
        /// <returns>アップデートが成功したかどうか</returns>
        Task<bool> UpdateModelAsync(LanguagePair languagePair, IProgress<double>? progressCallback = null);
    }
    
    /// <summary>
    /// ローカル翻訳モデル情報クラス
    /// </summary>
    public class LocalModelInfo
    {
        /// <summary>
        /// モデルID
        /// </summary>
        public string Id { get; set; } = string.Empty;
        
        /// <summary>
        /// 言語ペア
        /// </summary>
        public LanguagePair LanguagePair { get; set; } = null!;
        
        /// <summary>
        /// モデルサイズ（バイト）
        /// </summary>
        public long SizeInBytes { get; set; }
        
        /// <summary>
        /// バージョン
        /// </summary>
        public string Version { get; set; } = string.Empty;
        
        /// <summary>
        /// インストール済みかどうか
        /// </summary>
        public bool IsInstalled { get; set; }
        
        /// <summary>
        /// 推定翻訳品質（BLEU）
        /// </summary>
        public double EstimatedBleuScore { get; set; }
        
        /// <summary>
        /// 必要メモリ（MB）
        /// </summary>
        public int RequiredMemoryMB { get; set; }
        
        /// <summary>
        /// モデルファイルパス
        /// </summary>
        public string ModelFilePath { get; set; } = string.Empty;
        
        /// <summary>
        /// トークナイザーファイルパス
        /// </summary>
        public string TokenizerFilePath { get; set; } = string.Empty;
    }
    
    /// <summary>
    /// SentencePieceトークナイザーインターフェース
    /// </summary>
    public interface ISentencePieceTokenizer
    {
        /// <summary>
        /// トークナイザーをロードします
        /// </summary>
        /// <param name="modelPath">モデルファイルパス</param>
        /// <returns>ロードが成功したかどうか</returns>
        Task<bool> LoadModelAsync(string modelPath);
        
        /// <summary>
        /// テキストをエンコードします
        /// </summary>
        /// <param name="text">テキスト</param>
        /// <returns>トークンIDのリスト</returns>
        IReadOnlyList<int> Encode(string text);
        
        /// <summary>
        /// トークンIDをデコードします
        /// </summary>
        /// <param name="ids">トークンIDのリスト</param>
        /// <returns>デコードされたテキスト</returns>
        string Decode(IReadOnlyList<int> ids);
    }
    
    /// <summary>
    /// ローカル翻訳モデルクラス
    /// </summary>
    public class LocalTranslationModel : IDisposable
    {
        /// <summary>
        /// 言語ペア
        /// </summary>
        public LanguagePair LanguagePair { get; }
        
        /// <summary>
        /// モデル情報
        /// </summary>
        public LocalModelInfo ModelInfo { get; }
        
        /// <summary>
        /// テキストを翻訳します
        /// </summary>
        /// <param name="text">翻訳するテキスト</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>翻訳結果</returns>
        public Task<string> TranslateAsync(string text, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// 複数のテキストをバッチ翻訳します
        /// </summary>
        /// <param name="texts">翻訳するテキストのリスト</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>翻訳結果のリスト</returns>
        public Task<IReadOnlyList<string>> TranslateBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// リソースを解放します
        /// </summary>
        public void Dispose();
    }
}
```

## 実装上の注意点
- モデルのメモリ使用量を最小限に抑えるための最適化を行う
- 遅延ロード機構を実装して必要なモデルのみをメモリにロードする
- モデルファイルの破損検出と再ダウンロード機能を実装する
- 長文に対応するために自動分割処理を実装する
- 適切なエラー報告とロギングを実装する
- モデルの圧縮と最適化手法（量子化など）を検討する
- Issue #15（ONNX翻訳エンジン統合）との機能重複を避けるため、連携を検討する

## 関連Issue/参考
- 親Issue: #18 有料・無料プラン機能差別化とライセンス管理システム
- 関連: #9 翻訳システム基盤の構築
- 関連: #15 ONNX翻訳エンジン統合
- 関連: #21 翻訳キャッシュシステム
- 参照: E:\dev\Baketa\docs\baketa-ai-translation-requirements.md (3.1.2 Helsinki-NLP OPUS-MT ONNXモデルの詳細)
- 参照: E:\dev\Baketa\docs\baketa-ai-translation-requirements.md (3.1.2.2 モデルファイルの配布・管理戦略)
- 参照: E:\dev\Baketa\docs\baketa-ai-translation-requirements.md (3.1.2.3 SentencePieceの連携)

## マイルストーン
マイルストーン4: 機能拡張と最適化

## ラベル
- `type: feature`
- `priority: high`
- `component: translation`
- `business: core`
