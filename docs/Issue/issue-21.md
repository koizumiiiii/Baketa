# Issue 21: 翻訳キャッシュシステム

## 概要
Baketaアプリケーションに効率的な翻訳キャッシュシステムを実装し、API呼び出しコストの削減、レイテンシの短縮、リソース使用量の最適化を実現します。同一テキストの再翻訳を避け、セッション間でも翻訳結果を再利用することで、ユーザー体験とシステム性能を向上させます。

## 目的・理由
1. **API呼び出しコスト削減**: 同一テキストの再翻訳を避けることで、有料APIの使用料を削減する
2. **レイテンシ削減**: キャッシュヒット時には即座に結果を返すことで、ユーザー体験を向上させる
3. **リソース使用量の最適化**: 不要な処理を省くことでCPU使用率とメモリ使用量を削減する
4. **ゲーム内の繰り返しテキストへの対応**: 同じテキストが頻繁に表示されるゲームでの効率を大幅に向上させる

## 詳細
- 効率的なキャッシュキー設計の実装
- キャッシュ保持ポリシーの設計と実装
- セッション間でのキャッシュ永続化
- キャッシュ最適化と管理機能の実装

## タスク分解
- [ ] キャッシュキー設計
  - [ ] 原文テキスト、言語ペア、文脈ハッシュを組み合わせたキー設計の実装
  - [ ] 文脈ハッシュ化アルゴリズムの設計と実装
  - [ ] 類似性検出のための効率的なハッシュ関数の実装
  - [ ] キャッシュキー生成ユーティリティの実装
- [ ] インメモリキャッシュ
  - [ ] LRU（Least Recently Used）キャッシュの実装
  - [ ] LFU（Least Frequently Used）キャッシュの実装
  - [ ] スレッドセーフなキャッシュアクセスの実装
  - [ ] キャッシュサイズの動的調整機能
- [ ] 永続キャッシュ
  - [ ] SQLiteベースのキャッシュストレージの実装
  - [ ] 非同期キャッシュI/Oの実装
  - [ ] キャッシュの圧縮と最適化
  - [ ] 古いキャッシュエントリの自動クリーンアップ（7日間保持）
- [ ] キャッシュ管理
  - [ ] 最大キャッシュサイズ設定（メモリ10MB、永続50MB）
  - [ ] キャッシュヒット率の監視と統計収集
  - [ ] キャッシュクリア機能の実装
  - [ ] キャッシュプリロード機能（前回セッションの上位使用フレーズ）
- [ ] キャッシュ最適化
  - [ ] 頻出フレーズへの重み付け（LFU方式）
  - [ ] インデックス最適化によるルックアップ性能の向上
  - [ ] 文脈類似性に基づく柔軟なキャッシュマッチングの実装
  - [ ] キャッシュ使用状況の分析機能
- [ ] イベント通知
  - [ ] キャッシュヒット/ミスイベントの実装
  - [ ] キャッシュ統計イベントの実装
  - [ ] キャッシュ操作イベント（追加、更新、削除）の実装
- [ ] テストと検証
  - [ ] キャッシュヒット率の測定
  - [ ] パフォーマンス評価（速度向上、リソース使用量削減）
  - [ ] キャッシュサイズと保持期間の最適化
  - [ ] 長期使用における安定性テスト

## クラスとインターフェース設計案

```csharp
namespace Baketa.Translation.Cache
{
    /// <summary>
    /// 翻訳キャッシュインターフェース
    /// </summary>
    public interface ITranslationCache
    {
        /// <summary>
        /// キャッシュからアイテムを取得します
        /// </summary>
        /// <param name="key">キャッシュキー</param>
        /// <returns>キャッシュアイテム（存在しない場合はnull）</returns>
        Task<TranslationCacheItem?> GetAsync(string key);
        
        /// <summary>
        /// キャッシュにアイテムを追加または更新します
        /// </summary>
        /// <param name="key">キャッシュキー</param>
        /// <param name="item">キャッシュアイテム</param>
        /// <returns>操作が成功したかどうか</returns>
        Task<bool> SetAsync(string key, TranslationCacheItem item);
        
        /// <summary>
        /// キャッシュからアイテムを削除します
        /// </summary>
        /// <param name="key">キャッシュキー</param>
        /// <returns>操作が成功したかどうか</returns>
        Task<bool> RemoveAsync(string key);
        
        /// <summary>
        /// キャッシュをクリアします
        /// </summary>
        /// <returns>操作が成功したかどうか</returns>
        Task<bool> ClearAsync();
        
        /// <summary>
        /// キャッシュ統計を取得します
        /// </summary>
        /// <returns>キャッシュ統計</returns>
        Task<CacheStatistics> GetStatisticsAsync();
        
        /// <summary>
        /// 永続キャッシュをフラッシュします
        /// </summary>
        /// <returns>操作が成功したかどうか</returns>
        Task<bool> FlushPersistentCacheAsync();
    }
    
    /// <summary>
    /// キャッシュキーファクトリインターフェース
    /// </summary>
    public interface ICacheKeyFactory
    {
        /// <summary>
        /// キャッシュキーを生成します
        /// </summary>
        /// <param name="sourceText">元テキスト</param>
        /// <param name="sourceLang">元言語</param>
        /// <param name="targetLang">対象言語</param>
        /// <param name="context">翻訳コンテキスト（オプション）</param>
        /// <returns>キャッシュキー</returns>
        string CreateKey(string sourceText, string sourceLang, string targetLang, string? context = null);
        
        /// <summary>
        /// 文脈をハッシュ化します
        /// </summary>
        /// <param name="context">文脈テキスト</param>
        /// <returns>ハッシュ値</returns>
        string HashContext(string context);
        
        /// <summary>
        /// 類似文脈を検出します
        /// </summary>
        /// <param name="contextHash">文脈ハッシュ</param>
        /// <param name="threshold">類似度閾値</param>
        /// <returns>類似する文脈ハッシュのリスト</returns>
        Task<IReadOnlyList<string>> FindSimilarContextsAsync(string contextHash, double threshold = 0.8);
    }
    
    /// <summary>
    /// 翻訳キャッシュアイテムクラス
    /// </summary>
    public class TranslationCacheItem
    {
        /// <summary>
        /// 元テキスト
        /// </summary>
        public string SourceText { get; set; } = string.Empty;
        
        /// <summary>
        /// 翻訳テキスト
        /// </summary>
        public string TranslatedText { get; set; } = string.Empty;
        
        /// <summary>
        /// 元言語
        /// </summary>
        public string SourceLanguage { get; set; } = string.Empty;
        
        /// <summary>
        /// 対象言語
        /// </summary>
        public string TargetLanguage { get; set; } = string.Empty;
        
        /// <summary>
        /// 文脈ハッシュ（オプション）
        /// </summary>
        public string? ContextHash { get; set; }
        
        /// <summary>
        /// 翻訳エンジン
        /// </summary>
        public string Engine { get; set; } = string.Empty;
        
        /// <summary>
        /// 作成日時
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        
        /// <summary>
        /// 最終アクセス日時
        /// </summary>
        public DateTime LastAccessedAt { get; set; } = DateTime.Now;
        
        /// <summary>
        /// アクセス回数
        /// </summary>
        public int AccessCount { get; set; } = 1;
    }
    
    /// <summary>
    /// キャッシュ統計クラス
    /// </summary>
    public class CacheStatistics
    {
        /// <summary>
        /// インメモリキャッシュサイズ
        /// </summary>
        public int InMemoryCacheSize { get; set; }
        
        /// <summary>
        /// 永続キャッシュサイズ
        /// </summary>
        public int PersistentCacheSize { get; set; }
        
        /// <summary>
        /// ヒット数
        /// </summary>
        public int Hits { get; set; }
        
        /// <summary>
        /// ミス数
        /// </summary>
        public int Misses { get; set; }
        
        /// <summary>
        /// ヒット率
        /// </summary>
        public double HitRate => (Hits + Misses) > 0 ? (double)Hits / (Hits + Misses) : 0;
        
        /// <summary>
        /// インメモリキャッシュのメモリ使用量（バイト）
        /// </summary>
        public long InMemoryMemoryUsage { get; set; }
        
        /// <summary>
        /// 永続キャッシュのディスク使用量（バイト）
        /// </summary>
        public long PersistentDiskUsage { get; set; }
        
        /// <summary>
        /// 平均ルックアップ時間（ミリ秒）
        /// </summary>
        public double AverageLookupTimeMs { get; set; }
    }
}
```

## 実装上の注意点
- キャッシュアクセスは完全にスレッドセーフに実装する
- メモリ内キャッシュと永続化キャッシュの二層構造で効率化する
- キャッシュキーに文脈情報を含める量はヒット率に影響するため、適切なバランスを見つける
- 永続キャッシュのI/O操作はバックグラウンドスレッドで非同期に行う
- キャッシュサイズの上限を設定し、メモリ使用量を制御する
- 古いエントリの自動クリーンアップを実装し、キャッシュの鮮度を維持する
- セッション開始時のキャッシュプリロードは非同期で行い、起動遅延を防止する
- キャッシュヒット率を継続的に監視し、最適化のための分析データを収集する

## 関連Issue/参考
- 関連: #9 翻訳システム基盤の構築
- 関連: #19 クラウドAI翻訳連携（有料プラン向け）
- 関連: #20 ローカル翻訳モデル最適化（無料プラン向け）
- 参照: E:\dev\Baketa\docs\baketa-ai-translation-requirements.md (6.1.3 キャッシュ戦略)
- 参照: E:\dev\Baketa\docs\2-development\coding-standards\performance.md (4. キャッシュの効果的な利用)

## マイルストーン
マイルストーン4: 機能拡張と最適化

## ラベル
- `type: feature`
- `priority: medium`
- `component: translation`
- `performance: optimization`
