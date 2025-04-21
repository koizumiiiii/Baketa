# Issue 9-1: 翻訳エンジンインターフェースの設計と実装

## 概要
Baketaの翻訳機能の基礎となる翻訳エンジンインターフェースを設計・実装します。このインターフェースは、様々な翻訳エンジン（Web API、ローカルモデル）を統一的に扱うための抽象化レイヤーとなります。

## 目的・理由
翻訳エンジンインターフェースは、以下の理由で重要です：

1. 複数の翻訳エンジンを統一的なインターフェースで利用可能にする
2. 将来的な翻訳エンジンの追加や変更に柔軟に対応できる設計を提供する
3. テスト容易性を高め、モックを用いた単体テストを可能にする
4. 依存性注入による疎結合なコンポーネント設計を実現する

## 詳細
- 翻訳エンジンの基本インターフェースの設計
- 翻訳リクエスト・レスポンスモデルの設計
- 非同期処理をサポートした翻訳メソッドの実装
- エラーハンドリングの統一的なアプローチの設計

## タスク分解
- [ ] 基本インターフェース設計
  - [ ] `ITranslationEngine`インターフェースの設計
  - [ ] `ITranslationService`インターフェースの設計
  - [ ] インターフェース間の関係性定義
- [ ] 翻訳モデルクラスの設計
  - [ ] `TranslationRequest`クラスの設計と実装
  - [ ] `TranslationResponse`クラスの設計と実装
  - [ ] `TranslationError`クラスの設計と実装
- [ ] 言語管理クラスの設計
  - [ ] `Language`クラスの設計と実装
  - [ ] `LanguagePair`クラスの設計と実装
  - [ ] サポート言語管理機構の設計
- [ ] デフォルトの抽象実装クラスの作成
  - [ ] `TranslationEngineBase`抽象クラスの実装
  - [ ] 共通機能の実装
- [ ] テスト用モックエンジンの実装
  - [ ] `MockTranslationEngine`クラスの実装
  - [ ] テストシナリオの定義
- [ ] 単体テストの実装

## インターフェース設計案
```csharp
namespace Baketa.Translation.Abstractions
{
    /// <summary>
    /// 翻訳エンジンの機能を定義するインターフェース
    /// </summary>
    public interface ITranslationEngine : IDisposable
    {
        /// <summary>
        /// エンジン名を取得します
        /// </summary>
        string Name { get; }
        
        /// <summary>
        /// エンジンの説明を取得します
        /// </summary>
        string Description { get; }
        
        /// <summary>
        /// エンジンがオンライン接続を必要とするかどうかを示します
        /// </summary>
        bool RequiresNetwork { get; }
        
        /// <summary>
        /// サポートしている言語ペアを取得します
        /// </summary>
        /// <returns>サポートされている言語ペアのコレクション</returns>
        Task<IReadOnlyCollection<LanguagePair>> GetSupportedLanguagePairsAsync();
        
        /// <summary>
        /// 指定された言語ペアをサポートしているかどうかを確認します
        /// </summary>
        /// <param name="languagePair">確認する言語ペア</param>
        /// <returns>サポートしていればtrue</returns>
        Task<bool> SupportsLanguagePairAsync(LanguagePair languagePair);
        
        /// <summary>
        /// テキストを翻訳します
        /// </summary>
        /// <param name="request">翻訳リクエスト</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>翻訳レスポンス</returns>
        Task<TranslationResponse> TranslateAsync(
            TranslationRequest request, 
            CancellationToken cancellationToken = default);
            
        /// <summary>
        /// 複数のテキストをバッチ翻訳します
        /// </summary>
        /// <param name="requests">翻訳リクエストのコレクション</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>翻訳レスポンスのコレクション</returns>
        Task<IReadOnlyList<TranslationResponse>> TranslateBatchAsync(
            IReadOnlyList<TranslationRequest> requests, 
            CancellationToken cancellationToken = default);
            
        /// <summary>
        /// エンジンの準備状態を確認します
        /// </summary>
        /// <returns>準備ができていればtrue</returns>
        Task<bool> IsReadyAsync();
        
        /// <summary>
        /// エンジンを初期化します
        /// </summary>
        /// <returns>初期化が成功すればtrue</returns>
        Task<bool> InitializeAsync();
    }
    
    /// <summary>
    /// 翻訳サービスの機能を定義するインターフェース
    /// </summary>
    public interface ITranslationService
    {
        /// <summary>
        /// 利用可能な翻訳エンジンを取得します
        /// </summary>
        /// <returns>利用可能な翻訳エンジンのコレクション</returns>
        IReadOnlyList<ITranslationEngine> GetAvailableEngines();
        
        /// <summary>
        /// 現在アクティブな翻訳エンジンを取得します
        /// </summary>
        ITranslationEngine ActiveEngine { get; }
        
        /// <summary>
        /// 指定された名前のエンジンをアクティブにします
        /// </summary>
        /// <param name="engineName">アクティブにするエンジン名</param>
        /// <returns>成功すればtrue</returns>
        Task<bool> SetActiveEngineAsync(string engineName);
        
        /// <summary>
        /// テキストを翻訳します
        /// </summary>
        /// <param name="text">翻訳元テキスト</param>
        /// <param name="sourceLang">元言語</param>
        /// <param name="targetLang">対象言語</param>
        /// <param name="context">翻訳コンテキスト（オプション）</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>翻訳結果</returns>
        Task<TranslationResponse> TranslateAsync(
            string text, 
            Language sourceLang, 
            Language targetLang, 
            string? context = null, 
            CancellationToken cancellationToken = default);
            
        /// <summary>
        /// 複数のテキストをバッチ翻訳します
        /// </summary>
        /// <param name="texts">翻訳元テキストのコレクション</param>
        /// <param name="sourceLang">元言語</param>
        /// <param name="targetLang">対象言語</param>
        /// <param name="context">翻訳コンテキスト（オプション）</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>翻訳結果のコレクション</returns>
        Task<IReadOnlyList<TranslationResponse>> TranslateBatchAsync(
            IReadOnlyList<string> texts, 
            Language sourceLang, 
            Language targetLang, 
            string? context = null, 
            CancellationToken cancellationToken = default);
    }
    
    /// <summary>
    /// 翻訳リクエストを表すクラス
    /// </summary>
    public class TranslationRequest
    {
        /// <summary>
        /// 翻訳元テキスト
        /// </summary>
        public required string SourceText { get; set; }
        
        /// <summary>
        /// 元言語
        /// </summary>
        public required Language SourceLanguage { get; set; }
        
        /// <summary>
        /// 対象言語
        /// </summary>
        public required Language TargetLanguage { get; set; }
        
        /// <summary>
        /// 翻訳コンテキスト（オプション）
        /// </summary>
        public string? Context { get; set; }
        
        /// <summary>
        /// リクエストオプション
        /// </summary>
        public Dictionary<string, object?> Options { get; } = new();
        
        /// <summary>
        /// リクエストのユニークID
        /// </summary>
        public Guid RequestId { get; } = Guid.NewGuid();
    }
    
    /// <summary>
    /// 翻訳レスポンスを表すクラス
    /// </summary>
    public class TranslationResponse
    {
        /// <summary>
        /// 対応するリクエストのID
        /// </summary>
        public required Guid RequestId { get; set; }
        
        /// <summary>
        /// 翻訳元テキスト
        /// </summary>
        public required string SourceText { get; set; }
        
        /// <summary>
        /// 翻訳結果テキスト
        /// </summary>
        public string? TranslatedText { get; set; }
        
        /// <summary>
        /// 翻訳元言語
        /// </summary>
        public required Language SourceLanguage { get; set; }
        
        /// <summary>
        /// 翻訳先言語
        /// </summary>
        public required Language TargetLanguage { get; set; }
        
        /// <summary>
        /// 使用された翻訳エンジン名
        /// </summary>
        public required string EngineName { get; set; }
        
        /// <summary>
        /// 翻訳の信頼度スコア（0.0～1.0）
        /// </summary>
        public float ConfidenceScore { get; set; }
        
        /// <summary>
        /// 翻訳処理時間（ミリ秒）
        /// </summary>
        public long ProcessingTimeMs { get; set; }
        
        /// <summary>
        /// 翻訳が成功したかどうか
        /// </summary>
        public bool IsSuccess { get; set; }
        
        /// <summary>
        /// エラーが発生した場合のエラー情報
        /// </summary>
        public TranslationError? Error { get; set; }
        
        /// <summary>
        /// 追加のメタデータ
        /// </summary>
        public Dictionary<string, object?> Metadata { get; } = new();
    }
    
    /// <summary>
    /// 翻訳エラーを表すクラス
    /// </summary>
    public class TranslationError
    {
        /// <summary>
        /// エラーコード
        /// </summary>
        public required string ErrorCode { get; set; }
        
        /// <summary>
        /// エラーメッセージ
        /// </summary>
        public required string Message { get; set; }
        
        /// <summary>
        /// 詳細なエラー情報
        /// </summary>
        public string? Details { get; set; }
        
        /// <summary>
        /// エラーの原因となった例外
        /// </summary>
        public Exception? Exception { get; set; }
    }
    
    /// <summary>
    /// 言語を表すクラス
    /// </summary>
    public class Language
    {
        /// <summary>
        /// 言語コード（ISO 639-1）
        /// </summary>
        public required string Code { get; set; }
        
        /// <summary>
        /// 言語名（英語）
        /// </summary>
        public required string Name { get; set; }
        
        /// <summary>
        /// 言語名（現地語）
        /// </summary>
        public string? NativeName { get; set; }
        
        /// <summary>
        /// 言語の地域バリエーション（オプション）
        /// </summary>
        public string? RegionCode { get; set; }
        
        /// <summary>
        /// 言語が自動検出であるかどうか
        /// </summary>
        public bool IsAutoDetect { get; set; }
        
        /// <summary>
        /// 自動検出言語用の静的インスタンス
        /// </summary>
        public static Language AutoDetect { get; } = new Language
        {
            Code = "auto",
            Name = "Auto Detect",
            IsAutoDetect = true
        };
        
        /// <override />
        public override bool Equals(object? obj)
        {
            if (obj is not Language other)
                return false;
                
            return string.Equals(Code, other.Code, StringComparison.OrdinalIgnoreCase);
        }
        
        /// <override />
        public override int GetHashCode() => Code.ToLowerInvariant().GetHashCode();
        
        /// <override />
        public override string ToString() => $"{Name} ({Code})";
    }
    
    /// <summary>
    /// 言語ペアを表すクラス
    /// </summary>
    public class LanguagePair : IEquatable<LanguagePair>
    {
        /// <summary>
        /// 元言語
        /// </summary>
        public required Language SourceLanguage { get; set; }
        
        /// <summary>
        /// 対象言語
        /// </summary>
        public required Language TargetLanguage { get; set; }
        
        /// <override />
        public bool Equals(LanguagePair? other)
        {
            if (other is null)
                return false;
                
            return SourceLanguage.Equals(other.SourceLanguage) && 
                   TargetLanguage.Equals(other.TargetLanguage);
        }
        
        /// <override />
        public override bool Equals(object? obj) => Equals(obj as LanguagePair);
        
        /// <override />
        public override int GetHashCode()
        {
            return HashCode.Combine(SourceLanguage, TargetLanguage);
        }
        
        /// <override />
        public override string ToString() => $"{SourceLanguage.Code} -> {TargetLanguage.Code}";
    }
}
```

## 実装上の注意点
- 翻訳エンジンインターフェースは異なる翻訳エンジンの特性に対応できる柔軟性を持たせる
- 非同期処理とキャンセレーション対応を徹底する
- リソース管理（特にネットワーク接続）に注意し、適切にDispose処理を実装する
- エラー処理とロギングを統一的に扱える設計にする
- インターフェースの安定性を重視し、将来的な機能拡張を見据えた設計にする

## 関連Issue/参考
- 親Issue: #9 翻訳システム基盤の構築
- 関連Issue: #4 イベント集約機構の構築
- 参照: E:\dev\Baketa\docs\3-architecture\translation\translation-interfaces.md
- 参照: E:\dev\Baketa\docs\2-development\coding-standards\csharp-standards.md (3.1 非同期メソッドの命名規則)
- 参照: E:\dev\Baketa\docs\2-development\coding-standards\csharp-standards.md (3.4 キャンセレーション対応)

## マイルストーン
マイルストーン3: 翻訳とUI

## ラベル
- `type: feature`
- `priority: high`
- `component: translation`
