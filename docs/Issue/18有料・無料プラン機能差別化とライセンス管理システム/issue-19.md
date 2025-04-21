# Issue 19: クラウドAI翻訳連携（有料プラン向け）

## 概要
Baketaの有料プラン向けに、高品質な翻訳を提供するためのクラウドAI翻訳機能を実装します。Google Gemini APIとOpenAI APIを活用し、文脈を考慮した自然な翻訳体験を実現します。これにより、ゲーム内テキストの背景や状況を理解した、より高品質な翻訳結果をユーザーに提供します。

## 目的・理由
1. **高品質翻訳の提供**: 最新のAIモデルを活用した高品質な翻訳サービスを提供
2. **文脈理解の強化**: 前後の会話や状況を考慮した自然な翻訳を実現
3. **有料プランの価値向上**: 無料プランとの明確な品質差別化により有料プランの価値を高める
4. **翻訳精度と応答性のバランス**: 高品質な翻訳と迅速な応答性の両立を実現

## 詳細
- Google Gemini APIおよびOpenAI API連携の実装
- 効果的なプロンプトエンジニアリングの最適化
- 文脈管理システムの実装
- エラーハンドリングと回復戦略の実装

## タスク分解
- [ ] API連携基盤の実装
  - [ ] Google Gemini API接続機能の実装
  - [ ] OpenAI API接続機能の実装
  - [ ] API設定管理UIの実装
  - [ ] API切り替え機能の実装
  - [ ] API使用量と制限の管理機能
- [ ] プロンプトエンジニアリング
  - [ ] 最適なプロンプトテンプレートの設計と実装
  - [ ] APIパラメータの最適化（temperature等）
  - [ ] モデル別プロンプト調整機能の実装
  - [ ] プロンプト効果のテストと評価システム
- [ ] 文脈管理システム
  - [ ] 直前の会話（3～5行）バッファリング機能の実装
  - [ ] 文脈リセット条件の実装（時間、位置変化、画面変化）
  - [ ] 文脈サイズの最適化機能（設定可能範囲: 0～5行）
  - [ ] トークン数制限管理（最大500トークン）
- [ ] エラーハンドリングと回復戦略
  - [ ] API接続エラー時のリトライロジック（指数バックオフ方式）
  - [ ] 長期的な接続エラー時のフォールバック処理
  - [ ] API制限エラー検出と自動調整機能
  - [ ] ユーザー通知とステータス表示機能
- [ ] パフォーマンス最適化
  - [ ] 非同期処理による待ち時間の隠蔽
  - [ ] 効率的なHTTPクライアント設定
  - [ ] コネクションプーリングの実装
  - [ ] 翻訳結果キャッシュシステムとの連携
- [ ] テストとドキュメント
  - [ ] 各APIのモックを使用した単体テスト
  - [ ] エラーケースのテスト
  - [ ] パフォーマンステスト
  - [ ] API使用に関するドキュメント作成

## クラスとインターフェース設計案

```csharp
namespace Baketa.Translation.Cloud
{
    /// <summary>
    /// クラウドAI翻訳エンジンインターフェース
    /// </summary>
    public interface ICloudAiTranslationEngine : ITranslationEngine
    {
        /// <summary>
        /// 使用中のAPIプロバイダー名
        /// </summary>
        string ProviderName { get; }
        
        /// <summary>
        /// APIプロバイダーを切り替えます
        /// </summary>
        /// <param name="providerName">プロバイダー名（"Gemini", "OpenAI"など）</param>
        /// <returns>切り替えが成功したかどうか</returns>
        Task<bool> SwitchProviderAsync(string providerName);
        
        /// <summary>
        /// 文脈情報を追加します
        /// </summary>
        /// <param name="text">文脈テキスト</param>
        void AddContext(string text);
        
        /// <summary>
        /// 文脈をクリアします
        /// </summary>
        void ClearContext();
        
        /// <summary>
        /// API使用状況を取得します
        /// </summary>
        /// <returns>API使用状況</returns>
        Task<ApiUsageInfo> GetApiUsageInfoAsync();
    }
    
    /// <summary>
    /// 文脈管理インターフェース
    /// </summary>
    public interface IContextManager
    {
        /// <summary>
        /// 文脈にテキストを追加します
        /// </summary>
        /// <param name="text">追加するテキスト</param>
        void AddText(string text);
        
        /// <summary>
        /// 現在の文脈を取得します
        /// </summary>
        /// <returns>文脈テキスト</returns>
        string GetContext();
        
        /// <summary>
        /// 文脈をクリアします
        /// </summary>
        void Clear();
        
        /// <summary>
        /// 文脈をハッシュ化します
        /// </summary>
        /// <returns>ハッシュ値</returns>
        string GetContextHash();
        
        /// <summary>
        /// 文脈が変更された条件をチェックします
        /// </summary>
        /// <param name="screenRegion">画面領域</param>
        /// <param name="lastDetectionTime">前回の検出時間</param>
        /// <returns>リセットすべきかどうか</returns>
        bool ShouldResetContext(Rectangle? screenRegion, DateTime lastDetectionTime);
    }
}
```

## 実装上の注意点
- APIキーなどの機密情報は適切に暗号化して保存する
- APIの応答時間を考慮した非同期処理とタイムアウト設定を実装する
- レート制限に対処するための適切なスロットリングメカニズムを実装する
- 有効な文脈管理のためのリセット条件を慎重に調整する
- ネットワーク接続が不安定な場合のフォールバック処理を実装する
- キャッシュ戦略を適切に実装してAPI呼び出しを最小限に抑える
- 異なるAPIプロバイダーの特性に合わせた最適なプロンプト設計を行う

## 関連Issue/参考
- 親Issue: #18 有料・無料プラン機能差別化とライセンス管理システム
- 関連: #9 翻訳システム基盤の構築
- 関連: #21 翻訳キャッシュシステム
- 参照: E:\dev\Baketa\docs\baketa-ai-translation-requirements.md (5. AI翻訳連携の詳細)
- 参照: E:\dev\Baketa\docs\baketa-ai-translation-requirements.md (5.2 プロンプトエンジニアリング)
- 参照: E:\dev\Baketa\docs\baketa-ai-translation-requirements.md (5.3 文脈管理)

## マイルストーン
マイルストーン4: 機能拡張と最適化

## ラベル
- `type: feature`
- `priority: high`
- `component: translation`
- `business: premium`
