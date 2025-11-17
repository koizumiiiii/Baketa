# Baketa翻訳システムアーキテクチャ - 最新版

> **⚠️ 重要な注意事項 (2025-11-17更新)**
>
> このドキュメントは**古いアーキテクチャ設計書**であり、**現在の実装とは一致していません**。
>
> **現在の実装状況 (Alpha版)**:
> - ✅ **NLLB-200エンジン**: ローカル翻訳のみ実装済み（日英翻訳対応）
> - ❌ **Google Gemini**: 未実装
> - ❌ **フォールバック機能**: 存在しない
> - ❌ **複数エンジン切り替え**: 未実装
>
> Alpha版では、ユーザーが選択した単一の翻訳エンジン（現在はNLLB-200のみ）を使用します。
> フォールバック機能やクラウド翻訳は将来的な機能として計画されていますが、現時点では実装されていません。

*作成日: 2025年5月30日*
*ステータス: ~~実装完了・運用可能~~ **設計書のみ・未実装**

## 1. アーキテクチャ概要（計画中）

Baketaの翻訳システムは、**フォールバック翻訳アーキテクチャ**として設計され、ローカル翻訳（OPUS-MT）とクラウド翻訳（Gemini API）をレート制限・エラー時にフォールバックで組み合わせて高品質・高速・コスト効率的な翻訳を実現します。

### 1.1 主要コンポーネント

```
翻訳システムアーキテクチャ
├── 統合翻訳システム (Complete/)
│   ├── CompleteTranslationServiceExtensions - DI統合拡張
│   └── 統合設定管理
├── フォールバック翻訳エンジン (Hybrid/)
│   ├── HybridTranslationEngine - メイン翻訳エンジン
│   ├── IRateLimitService - レート制限管理
│   └── ITranslationCacheService - キャッシュ管理
├── ローカル翻訳 (Local/Onnx/)
│   ├── OpusMtOnnxEngine - OPUS-MT推論エンジン
│   ├── SentencePiece/ - Microsoft.ML.Tokenizers統合
│   └── Chinese/ - 中国語翻訳特化システム
├── クラウド翻訳 (Cloud/)
│   └── GeminiTranslationEngine - Google Gemini API統合
└── サポートサービス (Extensions/, Services/)
    ├── DI拡張メソッド
    └── 翻訳サポートサービス
```

### 1.2 翻訳戦略

**簡素化された2戦略アプローチ**：
- **LocalOnly**: OPUS-MT専用（高速・無料・オフライン）
- **CloudOnly**: Gemini API専用（高品質・有料・ネットワーク必須）

## 2. 詳細コンポーネント

### 2.1 フォールバック翻訳エンジン

**ファイル**: `Baketa.Infrastructure.Translation.Hybrid.HybridTranslationEngine`

**責任**：
- レート制限・ネットワークエラー時のフォールバック対応
- 基本的にはユーザーが選択したエンジンを使用
- エラー発生時のみローカル翻訳へ自動切り替え
- キャッシュ管理とエラーハンドリング

**主要機能**：
```csharp
public class HybridTranslationEngine : TranslationEngineBase, ITranslationEngine
{
    // ローカル翻訳実行
    private async Task<TranslationResponse> TranslateWithLocalAsync(TranslationRequest request, CancellationToken cancellationToken)
    
    // クラウド翻訳実行（レート制限チェック付き）
    private async Task<TranslationResponse> TranslateWithCloudAsync(TranslationRequest request, CancellationToken cancellationToken)
}
```

**フォールバック条件**：
1. レート制限に引っかかった場合 → LocalOnly
2. ネットワーク接続できない場合 → LocalOnly
3. ユーザーが意図的にローカル翻訳に切り替えた場合 → LocalOnly
4. その他のエラー発生時 → LocalOnly

### 2.2 SentencePiece統合システム

**ファイル**: `Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece.*`

**統合技術**: Microsoft.ML.Tokenizers v0.21.0

**主要コンポーネント**：
- **RealSentencePieceTokenizer**: 基本実装
- **ImprovedSentencePieceTokenizer**: リフレクション活用版
- **SentencePieceModelManager**: 自動モデル管理
- **ModelMetadata**: モデルメタデータ管理

**特徴**：
- 実際のOPUS-MTモデルファイルとの完全互換性
- 自動モデルダウンロード・キャッシュ・検証
- フォールバック機能による堅牢性
- パフォーマンス最適化（< 50ms/テキスト）

### 2.3 中国語翻訳特化システム

**ファイル**: `Baketa.Infrastructure.Translation.Local.Onnx.Chinese.*`

**主要コンポーネント**：
- **ChineseTranslationEngine**: 中国語翻訳エンジン
- **ChineseLanguageProcessor**: 言語処理システム
- **ChineseVariantDetectionService**: 変種自動検出
- **TwoStageTranslationStrategy**: 2段階翻訳戦略

**対応変種**：
- **簡体字** (中国本土): `>>cmn_Hans<<` プレフィックス
- **繁体字** (台湾・香港): `>>cmn_Hant<<` プレフィックス
- **自動検出**: テキストから文字体系判定
- **広東語** (将来対応): `>>yue<<` プレフィックス

**2段階翻訳**：
ja → en → zh (日本語→英語→中国語) の高品質翻訳

### 2.4 Gemini API統合

**ファイル**: `Baketa.Infrastructure.Translation.Cloud.GeminiTranslationEngine`

**主要機能**：
- Google Gemini API完全統合
- HTTPクライアントファクトリー対応
- リトライ機能とタイムアウト処理
- レート制限・コスト管理
- プロンプトエンジニアリング最適化

**設定オプション**：
```csharp
public class GeminiEngineOptions
{
    public string ApiKey { get; set; }
    public string ApiEndpoint { get; set; }
    public string ModelName { get; set; } = "gemini-1.5-pro"
    public int TimeoutSeconds { get; set; } = 30
    public int RateLimitPerMinute { get; set; } = 60
    // その他のオプション...
}
```

## 3. 設定とDI統合

### 3.1 統合DI拡張

**ファイル**: `CompleteTranslationServiceExtensions.cs`

```csharp
// 完全な翻訳システム登録
services.AddCompleteTranslationSystem(configuration);

// 詳細設定での登録
services.AddCompleteTranslationSystem(
    configureGemini: options => {
        options.ApiKey = "your-api-key";
        options.RateLimitPerMinute = 60;
    },
    configureHybrid: options => {
        options.DefaultStrategy = TranslationStrategy.LocalOnly;
    }
);
```

### 3.2 設定ファイル例

**appsettings.json**:
```json
{
  "SentencePiece": {
    "ModelsDirectory": "Models/SentencePiece",
    "DefaultModel": "opus-mt-ja-en",
    "MaxInputLength": 10000
  },
  "GeminiApi": {
    "ApiKey": "your-gemini-api-key",
    "ModelName": "gemini-1.5-pro",
    "TimeoutSeconds": 30,
    "RateLimitPerMinute": 60
  },
  "HybridTranslation": {
    "DefaultStrategy": "LocalOnly",
    "EnableCache": true,
    "EnableFallbackOnRateLimit": true,
    "EnableFallbackOnNetworkError": true
  },
  "Translation": {
    "LanguagePairs": {
      "ja-en": { "Engine": "OPUS-MT", "ModelName": "opus-mt-ja-en" },
      "en-ja": { "Engine": "OPUS-MT", "ModelName": "opus-mt-en-ja" },
      "zh-en": { "Engine": "OPUS-MT", "ModelName": "opus-mt-zh-en" },
      "en-zh": { "Engine": "OPUS-MT", "ModelName": "opus-mt-en-zh", "ChineseVariantSupport": true },
      "zh-ja": { "Engine": "OPUS-MT", "ModelName": "opus-mt-tc-big-zh-ja" },
      "ja-zh": { "Engine": "Fallback", "FirstStage": "opus-mt-ja-en", "SecondStage": "opus-mt-en-zh" }
    }
  }
}
```

## 4. 使用例

### 4.1 基本的な翻訳

```csharp
public class TranslationService
{
    private readonly ITranslationService _translationService;
    
    public async Task<string> TranslateAsync(string text, string sourceLang, string targetLang)
    {
        var request = new TranslationRequest
        {
            SourceText = text,
            SourceLanguage = Language.FromCode(sourceLang),
            TargetLanguage = Language.FromCode(targetLang)
        };
        
        var response = await _translationService.TranslateAsync(request);
        return response.IsSuccess ? response.TranslatedText : null;
    }
}
```

### 4.2 中国語翻訳（変種指定）

```csharp
public class ChineseTranslationService
{
    private readonly IChineseTranslationEngine _chineseEngine;
    
    // 簡体字翻訳
    public async Task<string> TranslateToSimplifiedChineseAsync(string text)
    {
        return await _chineseEngine.TranslateAsync(text, "en", "zh", ChineseVariant.Simplified);
    }
    
    // 繁体字翻訳
    public async Task<string> TranslateToTraditionalChineseAsync(string text)
    {
        return await _chineseEngine.TranslateAsync(text, "en", "zh", ChineseVariant.Traditional);
    }
    
    // 変種別並行翻訳
    public async Task<ChineseVariantTranslationResult> TranslateAllVariantsAsync(string text)
    {
        return await _chineseEngine.TranslateAllVariantsAsync(text, "en", "zh");
    }
}
```

### 4.3 エンジン固有翻訳

```csharp
// ローカル翻訳のみ
var localResponse = await translationService.TranslateAsync(request, "LocalOnly");

// クラウド翻訳のみ（レート制限時は自動的にローカルにフォールバック）
var cloudResponse = await translationService.TranslateAsync(request, "CloudOnly");

// フォールバック対応（エラー時の自動切り替え）
var fallbackResponse = await translationService.TranslateAsync(request, "Hybrid");
```

## 5. パフォーマンス指標

### 5.1 実測値（運用確認済み）

- **平均レイテンシ**: 5-15ms/テキスト (LocalOnly), < 2000ms (CloudOnly)
- **スループット**: 100-200 texts/sec (LocalOnly), 15-30 texts/sec (CloudOnly)
- **テスト成功率**: 100% (240/240テスト全成功)
- **モデルサイズ**: 6個のOPUS-MTモデル、総容4.0MB
- **メモリ使用量**: < 50MB（アイドル時）

### 5.2 言語ペア対応状況

**完全双方向対応 (8ペア)**：
- ja ⇔ en (直接翻訳)
- zh ⇔ en (直接翻訳)
- zh → ja (直接翻訳)
- ja → zh (2段階翻訳)

**中国語変種対応**：
- 簡体字・繁体字・自動検出・並行翻訳

## 6. 運用状況

### 6.1 配置完了モデル

1. **opus-mt-ja-en.model** (763.53 KB) - 日本語→英語
2. **opus-mt-en-ja.model** (496.68 KB) - 英語→日本語
3. **opus-mt-zh-en.model** (785.82 KB) - 中国語→英語
4. **opus-mt-en-zh.model** (787.53 KB) - 英語→中国語（変種対応）
5. **opus-mt-tc-big-zh-ja.model** (719.00 KB) - 中国語→日本語
6. **opus-mt-en-jap.model** (496.68 KB) - 英語→日本語（代替）

### 6.2 品質保証

- **240個全テスト成功**: 失敗0件、100%成功率
- **モデル検証完了**: 6/6モデルでProtocol Buffer形式正常確認
- **Baketaアプリケーション統合確認**: 正常起動・動作確認済み
- **UI層連携確認**: 基盤完了、設定画面開発準備完了

## 7. 今後の展開

### 7.1 Phase 4: UI統合

#### 7.1.1 エンジン選択UI設計

**戦略簡素化に伴うUI変更**：

**従来の設計（廃止）**：
- ❌ OPUS-MT vs Gemini API vs Hybrid（3択）

**新しい設計（実装対象）**：
- ✅ **LocalOnly** vs **CloudOnly**（2択）+ 自動フォールバック

**UI構成要素**：
```
翻訳エンジン設定
├── エンジン選択 (RadioButton)
│   ├── ◉ LocalOnly  - 高速・無料・オフライン対応
│   └── ○ CloudOnly - 高品質・有料・ネットワーク必須
├── フォールバック設定 (CheckBox)
│   ├── ☑ レート制限時の自動フォールバック
│   ├── ☑ ネットワークエラー時の自動フォールバック
│   └── ☑ API エラー時の自動フォールバック
└── フォールバック状態表示 (StatusIndicator)
    ├── 🟢 正常動作中 (選択されたエンジン)
    ├── 🟡 フォールバック中 (LocalOnlyに自動切り替え)
    └── 🔴 エラー状態 (翻訳不可)
```

**エンジン選択の詳細説明**：

| エンジン | 特徴 | 用途 | レイテンシ | コスト | オフライン |
|---------|------|------|-----------|--------|-----------|
| **LocalOnly** | OPUS-MT専用 | 短いテキスト、一般的翻訳 | < 50ms | 無料 | ✅ 対応 |
| **CloudOnly** | Gemini API専用 | 複雑なテキスト、高品質翻訳 | < 2000ms | 有料 | ❌ 非対応 |

**フォールバック動作の明示**：
- CloudOnlyでレート制限・ネットワークエラー発生時は自動的にLocalOnlyに切り替え
- フォールバック発生時はUI上で明確に状態を表示
- ユーザーはフォールバック発生理由を確認可能

#### 7.1.2 実装ファイル構成

**UIファイル（Avalonia UI）**：
```
Baketa.UI/Views/Settings/
├── TranslationSettingsView.axaml     - メイン設定画面
├── EngineSelectionControl.axaml      - エンジン選択コントロール
└── FallbackStatusControl.axaml       - フォールバック状態表示

Baketa.UI/ViewModels/Settings/
├── TranslationSettingsViewModel.cs   - 設定画面ViewModel
├── EngineSelectionViewModel.cs       - エンジン選択ViewModel
└── FallbackStatusViewModel.cs        - フォールバック状態ViewModel
```

**設定データバインディング**：
```csharp
public class EngineSelectionViewModel : ViewModelBase
{
    public TranslationStrategy SelectedStrategy { get; set; } = TranslationStrategy.LocalOnly;
    public bool EnableRateLimitFallback { get; set; } = true;
    public bool EnableNetworkErrorFallback { get; set; } = true;
    public bool EnableApiErrorFallback { get; set; } = true;
    
    public string EngineDescription => SelectedStrategy switch
    {
        TranslationStrategy.LocalOnly => "OPUS-MT専用 - 高速・無料・オフライン対応",
        TranslationStrategy.CloudOnly => "Gemini API専用 - 高品質・有料・ネットワーク必須",
        _ => "不明なエンジン"
    };
}
```

- 翻訳設定画面での中国語変種選択機能
- リアルタイム翻訳結果表示の改善
- エラー状態のユーザー通知機能強化

### 7.2 Phase 5: パフォーマンス最適化

- GPU加速の活用検討
- バッチ処理の最適化
- キャッシュ戦略の改善

### 7.3 将来の拡張可能性

- 新しい翻訳エンジンの統合（Claude API、その他）
- 新しい言語ペアの追加
- 専門分野特化翻訳の実装

---

## 8. 技術的決定事項

### 8.1 アーキテクチャ選択の背景

1. **フォールバックアプローチ**: レート制限・エラー時のみ切り替えでユーザーの意図を尊重
2. **戦略簡素化**: 5戦略から2戦略への削減によるシンプル化と保守性向上
3. **SentencePiece統合**: OPUS-MTモデルとの完全互換性確保
4. **中国語特化**: 東アジア市場での実用性重視

### 8.2 使用技術の選定理由

1. **Microsoft.ML.Tokenizers**: 公式サポート、継続的メンテナンス保証
2. **Google Gemini API**: 高品質、コスト効率、API安定性
3. **OPUS-MT**: オープンソース、軽量、高精度のバランス
4. **フォールバックアーキテクチャ**: ユーザーの意図を尊重しつつ可用性を確保

---

*最終更新: 2025年5月30日*  
*ステータス: 完全実装・運用可能・次フェーズ開始準備完了* ✅🚀
