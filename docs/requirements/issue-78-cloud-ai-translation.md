# Issue #78: Cloud AI Translation Integration 要件定義書

## 1. 概要

### 1.1 目的
有料プラン（Pro/Premia）ユーザー向けに、画像直接入力によるCloud AI翻訳機能を提供する。
ローカルOCRとCloud AIを**並列実行**し、高精度な翻訳と正確な座標情報を両立する。

### 1.2 アーキテクチャ概要
```
キャプチャ取得
    │
    ├──────────────────┬──────────────────┐
    ▼                  ▼                  │
【並列A】           【並列B】              │
ローカルOCR         Cloud AI              │
(Surya)            (Gemini/OpenAI)        │
    │                  │                  │
    ▼                  ▼                  │
座標+生テキスト    原文+翻訳文(JSON)       │
    │                  │                  │
    └──────────┬───────┘                  │
               ▼                          │
         マッチング・統合                  │
               │                          │
               ▼                          │
         オーバーレイ表示 ◄────────────────┘
                              (フォールバック時)
```

### 1.3 対象プラン
| プラン | ローカル翻訳 | クラウド翻訳 | 月間トークン上限 |
|--------|------------|------------|----------------|
| Free | ✅ | ❌ | 500回/月 |
| Standard | ✅ | ❌ | 無制限 |
| Pro | ✅ | ✅ | 4,000,000トークン |
| Premia | ✅ | ✅ | 8,000,000トークン |

---

## 2. 設計原則

### 2.1 モデル非依存設計
- **クラス名・メソッド名に具体的なAIモデル名（gemini, gpt等）を含めない**
- AIモデルは今後新しいものに乗り換える可能性がある
- プロバイダー切り替えは設定ファイル（appsettings.json）とDIで行う
- コード変更なしでモデル変更が可能な設計にする

### 2.2 既存処理への非影響
- **Free/Standardプランの既存翻訳処理に影響を与えない**
- 既存の `TranslationOrchestrationService` のフローは維持
- Cloud AI翻訳は Pro/Premia プラン限定で別パイプラインとして追加
- ローカル翻訳（NLLB）は従来通り動作を保証

### 2.3 段階的導入
- 既存機能を壊さずに新機能を追加
- フィーチャーフラグで有効/無効を切り替え可能
- 問題発生時は即座にローカル翻訳にフォールバック

---

## 3. Cloud AI構成

### 3.1 使用モデル（設定ファイルで変更可能）

**⚠️ 設計原則**: モデルは設定ファイルで指定し、コードにハードコードしない

| 優先度 | 役割 | 現在のモデル | 用途 |
|--------|------|-------------|------|
| 1 | Primary | Gemini 2.5 Flash-Lite | 低コスト・高速・画像対応 |
| 2 | Secondary | GPT-4.1-nano | Primaryフォールバック用 |
| 3 | Local | NLLB-200 | 全クラウド障害時 |

※ 将来のモデル変更時はappsettings.jsonの設定変更のみで対応

### 3.2 フォールバック条件

```
Primary Cloud AI
    │
    ├─ 成功 → 翻訳結果を使用
    │
    └─ 失敗 (API障害/タイムアウト/レート制限)
           │
           ▼
       Secondary Cloud AI
           │
           ├─ 成功 → 翻訳結果を使用
           │
           └─ 失敗
                  │
                  ▼
              Local Translation (NLLB)
                  │
                  └─ テキストベース翻訳
                     (OCRテキストを入力として使用)
```

### 3.3 フォールバック自動復帰
- フォールバック後 **5分経過** で Primary Cloud AI を再試行
- 再試行成功 → Primary に自動復帰
- 再試行失敗 → 再度5分待機

---

## 4. 並列処理アーキテクチャ

### 4.1 処理フロー詳細

#### 【並列A】ローカルOCR (Surya)
- **目的**: 正確な「座標 (Bounding Box)」と「生テキスト (Raw Text)」の取得
- **出力**: `List<OcrChunk> { Text, BoundingBox }`
- **処理時間目標**: < 500ms

#### 【並列B】Cloud AI (Gemini/OpenAI)
- **目的**: 文脈を理解した「高精度翻訳」と「原文の再構成」
- **入力**: **画像そのもの**（テキストは送らない）
- **出力**: `List<TranslationResult> { OriginalText, TranslatedText }` (JSON形式)
- **処理時間目標**: < 3000ms

### 4.2 System Instruction（プロンプト）

```text
あなたはゲーム画面の翻訳アシスタントです。
画像内のテキストを読み取り、以下のJSON形式のリストで出力してください。
座標情報は不要です。原文と翻訳文のペアのみを返してください。

[
  {
    "original_text": "原文（改行はスペースに置換）",
    "translated_text": "文脈を考慮した自然な日本語翻訳"
  }
]
```

**ポイント**:
- AIに座標を計算させない（トークン消費増・精度低下の原因）
- 座標はローカルOCRに任せる

---

## 5. マッチング・統合ロジック

### 5.1 ファジーマッチング (Fuzzy Matching)

OCRの誤認識（`o` と `a` の間違いなど）を許容するため、**レーベンシュタイン距離（編集距離）**を使用。

```csharp
// 類似度計算
double similarity = 1.0 - (double)LevenshteinDistance(localText, aiOriginalText)
                         / Math.Max(localText.Length, aiOriginalText.Length);

// 80%以上で同一テキストとみなす
if (similarity >= 0.80)
{
    // マッチ成功 → AI翻訳を採用
}
```

### 5.2 包含マッチング (Containment / Reverse Lookup)

ローカルOCRが改行などでテキストを分割してしまっている場合の対策。

**ケース例**:
- **Local**: `["This is a", "pen."]` (2つのチャンク)
- **AI**: `Original: "This is a pen."`

**ロジック**:
1. AIの `OriginalText` 内に、ローカルの `Chunk` が **部分一致** で含まれているか検索
2. 連続する複数のローカルチャンクが1つのAIテキストに含まれている場合、**強制統合（Force Merge）**
3. 統合されたローカル座標（`UnionRect`）に対して、AI翻訳を表示

### 5.3 AI座標フォールバック (Phase 3) ⚠️ 実験的機能

ローカルOCRが文字を見落とした場合の対策。

**⚠️ Geminiレビュー指摘**: この機能はリスクが高い
- AIに座標を出力させることはハルシネーション（幻覚）を誘発しやすい
- 精度が保証できない
- **デフォルトでは無効とし、ユーザーが明示的に有効にする設定とする**

**条件**:
- AIは読めているが、ローカルOCRが検出に失敗（座標がない）

**実装**:
1. AIに「大まかな座標」もリクエスト（オプション）
2. ローカルが見落とした場合のみ、AI座標をフォールバックとして使用
3. ただし位置ズレのリスクがあるため、信頼度フラグを付与
4. **設定画面で「実験的機能」として表示、デフォルトOFF**

---

## 6. エッジケース対策

### 6.1 ハルシネーション対策

**現象**: 画面に存在しない文字をAIが「ある」と言って返してくる

**対策**:
- マッチングロジックで「ローカルOCR側に該当するテキスト（または類似テキスト）が一つも見つからない」場合
- → **そのAI翻訳結果を破棄（表示しない）**

### 6.2 OCR見落とし対策

**現象**: AIは読めているが、ローカルOCRが検出に失敗

**対策**:
- Phase 3でAI座標フォールバックを実装
- 位置ズレのリスクがあるため、ユーザー設定で有効/無効を切り替え可能に

---

## 7. トークン消費追跡システム

### 7.1 トークン計算方法

画像入力の場合、トークン数が大きくなるため**ハイブリッド計算**を採用。

1. **事前推定（画像サイズベース）**
   - リクエスト前に上限チェック
   - 画像解像度からトークン数を推定

2. **事後確定（APIレスポンス）**
   - `usage` フィールドから正確なトークン数を取得
   - 推定との差分を記録

### 7.2 画像トークン推定式

```
Gemini: tokens ≈ (width × height) / 750
OpenAI: tokens ≈ (width × height) / 512 × 85 (タイルベース)
```

### 7.3 使用量追跡

```csharp
interface ITokenConsumptionTracker
{
    Task RecordUsageAsync(int tokensUsed, string engineId, TokenUsageType type);
    Task<TokenUsageInfo> GetMonthlyUsageAsync();
    Task<int> GetRemainingTokensAsync();
    Task<bool> IsLimitExceededAsync();
    Task<double> GetUsagePercentageAsync();
    Task<int> EstimateImageTokensAsync(int width, int height, string provider);
}
```

---

## 8. 実装コンポーネント

### 8.1 新規インターフェース

| インターフェース | 説明 |
|-----------------|------|
| `ICloudAITranslator` | 画像→翻訳の統合インターフェース |
| `ITranslationMerger` | ローカルOCR + AI翻訳の統合ロジック |
| `ITokenConsumptionTracker` | トークン消費追跡 |
| `IEngineAccessController` | プラン別エンジン制限 |
| `IFallbackStrategy` | フォールバック戦略 |

### 8.2 新規クラス

**⚠️ 設計原則: モデル名を処理名に含めない**
- AIモデルは今後変更する可能性があるため、クラス名・メソッド名に具体的なモデル名（gemini, gpt等）を含めない
- プロバイダー切り替えは設定ファイルとDIで行う

| クラス | 層 | 説明 |
|--------|-----|------|
| `CloudImageTranslator` | Infrastructure | クラウドAI画像翻訳の基底クラス |
| `PrimaryCloudTranslator` | Infrastructure | メインAI実装（現在: Gemini） |
| `SecondaryCloudTranslator` | Infrastructure | サブAI実装（現在: OpenAI） |
| `FuzzyTextMatcher` | Core | ファジーマッチング実装 |
| `ChunkMerger` | Core | チャンク統合ロジック |
| `ParallelTranslationOrchestrator` | Application | 並列処理オーケストレーション |
| `TokenConsumptionTracker` | Infrastructure | トークン追跡実装 |
| `CloudTranslatorFactory` | Infrastructure | プロバイダー切り替えファクトリー |
| `EngineStatusManager` | Application | フォールバック状態一元管理（Geminiレビュー追加） |

### 8.2.1 EngineStatusManager（Geminiレビュー追加）

フォールバック状態を一元管理するシングルトンサービス。

```csharp
interface IEngineStatusManager
{
    bool IsEngineAvailable(string engineId);
    void MarkEngineUnavailable(string engineId, TimeSpan duration);
    void MarkEngineAvailable(string engineId);
    DateTime? GetNextRetryTime(string engineId);
    EngineStatus GetStatus(string engineId);
}

// 利用例: フォールバック期間中はプライマリへの不要なAPIコールをスキップ
if (!_engineStatusManager.IsEngineAvailable("primary"))
{
    // Primaryスキップ、Secondaryに直接フォールバック
}
```

### 8.3 既存クラスへの影響

**⚠️ 設計原則: Free/Standardプランの既存処理に影響を与えない**

| クラス | 変更内容 | 影響範囲 |
|--------|----------|----------|
| `TranslationOrchestrationService` | Pro/Premia時のみ並列パイプライン使用 | Pro/Premia限定 |
| `UserPlanService` | Patreon連携実装 | 全プラン |
| 既存ローカル翻訳処理 | **変更なし** | Free/Standard |

### 8.3.1 責務分離の明確化（Geminiレビュー追加）

**問題**: `TranslationOrchestrationService`にプラン分岐と並列処理を追加すると責務が肥大化

**対策**:
- `TranslationOrchestrationService`の責務を**パイプライン選択のみ**に限定
- 複雑なロジック（並列処理、マージ、フォールバック）はすべて`ParallelTranslationOrchestrator`に委譲

```csharp
// TranslationOrchestrationService（責務限定版）
public async Task TranslateAsync(...)
{
    var plan = await _userPlanService.GetCurrentPlanAsync();

    if (plan >= PlanType.Pro && _settings.EnableCloudAI)
    {
        // Pro/Premia: 新しい並列パイプラインに完全委譲
        await _parallelOrchestrator.TranslateAsync(...);
    }
    else
    {
        // Free/Standard: 既存のローカル翻訳フロー（変更なし）
        await _existingLocalTranslator.TranslateAsync(...);
    }
}
```

### 8.4 プロバイダー抽象化

```csharp
// プロバイダーに依存しない設計
interface ICloudTranslatorProvider
{
    string ProviderId { get; }  // "primary", "secondary" 等
    Task<ImageTranslationResult> TranslateImageAsync(byte[] image, ...);
}

// 設定ファイルでプロバイダーを指定
{
    "CloudTranslation": {
        "PrimaryProvider": "gemini-2.5-flash-lite",
        "SecondaryProvider": "gpt-4.1-nano",
        "Providers": {
            "gemini-2.5-flash-lite": { "ApiEndpoint": "...", "Model": "..." },
            "gpt-4.1-nano": { "ApiEndpoint": "...", "Model": "..." }
        }
    }
}
```

---

## 9. 実装計画

**⚠️ Geminiレビュー推奨: 段階的リリースアプローチ**

マッチングロジックは複雑であり、エッジケースが多いため、段階的にリリースすることを推奨。

### Phase 1: 基盤整備
1. ITokenConsumptionTracker 実装（画像トークン推定含む）
2. TokenUsageRepository 実装（永続化）
3. IEngineAccessController 実装
4. UserPlanService 実装（Patreon連携）
5. EngineStatusManager 実装（フォールバック状態管理）

### Phase 2: Cloud AI実装
1. CloudImageTranslator 基底クラス実装
2. PrimaryCloudTranslator 実装（現在: Gemini 2.5 Flash-Lite）
3. SecondaryCloudTranslator 実装（現在: GPT-4.1-nano）
4. IFallbackStrategy 実装（3段階フォールバック）
5. System Instruction（プロンプト）最適化

### Phase 3: 統合ロジック（MVP）
**⭐ 最初のリリースターゲット**

1. **FuzzyTextMatcher 実装のみ**（レーベンシュタイン距離、80%閾値）
2. ローカルOCRで座標が取得できたテキストに対してのみAI翻訳を適用
3. ハルシネーション検出・破棄ロジック
4. **包含マッチングとAI座標は後続フェーズで追加**

### Phase 3.5: 統合ロジック（拡張）- 後続リリース
**実際の利用データを収集・分析した上で実装**

1. ChunkMerger 実装（包含マッチング）
2. AI座標フォールバック実装（実験的機能として）
3. 追加のエッジケース対応

### Phase 4: オーケストレーション
1. ParallelTranslationOrchestrator 実装
2. TranslationOrchestrationService 統合
3. エラーハンドリング・リトライロジック
4. パフォーマンス最適化

### Phase 5: UI統合
1. エンジン選択UI更新
2. 使用量表示実装
3. アラート通知実装
4. 設定画面更新（AI座標フォールバック有効/無効）

### Phase 6: テストと検証
1. 単体テスト作成
2. 統合テスト作成
3. E2Eテスト（手動）
4. パフォーマンステスト

---

## 10. 確定事項（ユーザー確認済み）

1. **メインAIモデル**: Gemini 2.5 Flash-Lite
2. **サブAIモデル**: GPT-4.1-nano
3. **フォールバック順序**: Gemini → OpenAI → NLLB
4. **実装範囲**: Phase 3（全機能：AI座標フォールバック含む）
5. **トークン計算**: APIレスポンス + 画像サイズ推定（ハイブリッド）
6. **フォールバック自動復帰**: 5分後に再試行
7. **コンテキストサイズ**: デフォルト5行
8. **単一デバイス強制**: Issue #233/234で実装済み

---

## 11. 依存関係

```
ITokenConsumptionTracker
    └── TokenUsageRepository
    └── IUserPlanService (プラン上限取得)
    └── ImageTokenEstimator (画像サイズ推定)

ICloudAITranslator
    └── GeminiImageTranslator
    └── OpenAIImageTranslator
    └── IFallbackStrategy

ITranslationMerger
    └── FuzzyTextMatcher
    └── ChunkMerger
    └── AI座標フォールバック

ParallelTranslationOrchestrator
    └── ISuryaOcrService (並列A)
    └── ICloudAITranslator (並列B)
    └── ITranslationMerger
    └── ITokenConsumptionTracker
```

---

## 12. 受け入れ基準

### 12.1 機能要件
- [ ] Pro/Premiaユーザーのみクラウド翻訳が利用可能
- [ ] Gemini 2.5 Flash-Lite で画像入力翻訳が動作
- [ ] Gemini障害時にGPT-4.1-nanoへフォールバック
- [ ] 両方障害時にNLLBへフォールバック
- [ ] 5分後にメインAIへ自動復帰
- [ ] ファジーマッチング（80%類似度）でテキスト統合
- [ ] チャンク強制統合が正常動作
- [ ] AI座標フォールバックが動作（設定で有効/無効）
- [ ] ハルシネーション検出・破棄が動作
- [ ] 月間トークン使用量が正確に追跡される
- [ ] 使用量アラート（80%/90%/100%）が表示

### 12.2 非機能要件
- [ ] 並列処理の合計時間 < 3秒（95パーセンタイル）
- [ ] フォールバック切り替え時間 < 1秒
- [ ] ファジーマッチング処理時間 < 100ms
- [ ] トークン使用量の永続化が正常動作
- [ ] アプリ再起動後も使用量が保持される

### 12.3 UI/UX要件
- [ ] エンジン選択UIが直感的
- [ ] フォールバック状態がステータスバーに表示
- [ ] 使用量表示が分かりやすい
- [ ] AI座標使用時は視覚的に区別可能
- [ ] **翻訳処理中インジケーター表示**（Geminiレビュー追加）
  - Cloud AI応答時間はネットワーク依存のため、「クラウド翻訳中...」インジケーターを明確に表示
  - タイムアウト時は速やかにフォールバック

---

## 13. 技術的考慮事項

### 13.1 画像最適化
- 送信前に画像をリサイズ（トークン節約）
- 推奨解像度: 1280x720 以下
- フォーマット: JPEG（品質80%）

### 13.2 並列処理の同期
- `Task.WhenAll` で並列実行
- タイムアウト: ローカルOCR 2秒、Cloud AI 10秒
- 片方がタイムアウトしても、もう片方の結果で続行可能

### 13.3 セキュリティ（Geminiレビュー反映 v2）

#### 13.3.1 Relay Serverアーキテクチャ（必須）

**⚠️ 重要: ユーザーはAPIキーを持たない。すべてのCloud AI呼び出しはRelay Server経由で行う。**

```
┌─────────────┐    ┌─────────────────┐    ┌───────────────┐
│   ユーザー   │───▶│  Relay Server   │───▶│  Gemini/OpenAI │
│  (Baketa)   │    │ (APIキー保持)    │    │     API        │
└─────────────┘    └─────────────────┘    └───────────────┘
       │                    │
       │  Patreon認証       │  トークン消費記録
       ▼                    ▼
┌─────────────┐    ┌─────────────────┐
│   Patreon   │    │   Supabase DB   │
└─────────────┘    └─────────────────┘
```

**フロー**:
1. ユーザー: PatreonでPro/Premia購入
2. Baketa: Patreon OAuth認証でJWTトークン取得
3. Baketa: 画像をRelay Serverに送信（JWTトークン付き）
4. Relay Server: JWTトークン検証 → プラン確認 → トークン残量チェック → Cloud AI呼び出し → 消費記録
5. Baketa: 翻訳結果を受信

#### 13.3.2 Relay Server セキュリティ要件

**画像データ保護**:
- 画像データはメモリ上でのみストリーム処理、ディスク保存禁止
- ロギングには画像ハッシュ・サイズのみ記録、本体は記録しない
- プライバシーポリシーで「画像は翻訳処理のためにのみ転送、サーバー保存なし」を明記

**APIキー保護**:
- 環境変数で管理（Vercel/Railway等のシークレット機能を使用）
- 推奨: AWS Secrets Manager / HashiCorp Vault / Doppler 等の専用シークレット管理サービス
- 3〜6ヶ月ごとのAPIキーローテーション運用ルール策定

**トークン消費の不正回避**:
- JWTの有効期間を15分〜1時間に短縮、リフレッシュトークン導入
- アトミックなDB操作: `SELECT ... FOR UPDATE` で競合状態防止
- リプレイアタック対策: JWTに `jti` (JWT ID) 追加、Upstash Redis等でキャッシュして重複拒否

**レート制限**:
- ユーザー単位のレートリミッター導入（`@upstash/ratelimit` + Upstash Redis）
- 翻訳エンドポイント: 10リクエスト/分
- 全体: 100リクエスト/分

**コスト管理**:
- ハードリミット: 月間/日次の厳格な利用上限
- サーキットブレーカー: エラー率急上昇時に自動停止
- Google Cloud/OpenAI課金アラート: 予算80%で管理者通知

#### 13.3.3 クライアント側セキュリティ

**画像データのメモリ管理**:
- `IDisposable`の徹底: すべての画像関連クラスで実装、`using`ステートメント必須
- `Span<T>`と`Memory<T>`の活用: 不要なバイト配列コピーを防止
- ネイティブリソース解放の確認: C++/WinRT側のキャプチャリソース解放をクロスチェック

```csharp
// 画像処理時の安全なパターン
using var imageStream = new MemoryStream(capturedImage);
Span<byte> imageSpan = capturedImage.AsSpan();
// 処理完了後、明示的にクリア
Array.Clear(capturedImage, 0, capturedImage.Length);
```

**OAuth セキュリティ**:
- `state`パラメータにランダム値を設定、コールバック時に検証（CSRF対策）

#### 13.3.4 通信の安全性
- HTTPS必須（TLS 1.2以上）
- APIリクエストにリトライ制限
- レート制限超過時の適切なバックオフ

---

## 14. Relay Server 実装要件

### 14.1 新規エンドポイント

Relay Server（`patreon-relay-server`）に以下のエンドポイントを追加:

| エンドポイント | メソッド | 説明 |
|---------------|---------|------|
| `/api/translate` | POST | 画像翻訳リクエスト |
| `/api/usage` | GET | トークン使用量取得 |
| `/api/usage/remaining` | GET | 残りトークン数取得 |

### 14.2 `/api/translate` 仕様

**リクエスト**:
```json
{
  "image": "base64エンコードされた画像",
  "source_language": "en",
  "target_language": "ja",
  "provider": "primary"  // "primary" or "secondary"
}
```

**レスポンス**:
```json
{
  "success": true,
  "translations": [
    { "original_text": "Hello", "translated_text": "こんにちは" }
  ],
  "tokens_used": 1234,
  "remaining_tokens": 3998766
}
```

### 14.3 環境変数設定

```bash
# Relay Server環境変数（Cloudflare Workers wrangler secret で設定）
GEMINI_API_KEY=your_gemini_api_key
OPENAI_API_KEY=your_openai_api_key
```

### 14.4 APIキー取得手順

#### Gemini API Key
1. [Google AI Studio](https://aistudio.google.com/) にアクセス
2. Googleアカウントでログイン
3. 「Get API Key」→「Create API Key」をクリック
4. 生成されたキーをコピー

#### OpenAI API Key
1. [OpenAI Platform](https://platform.openai.com/) にアクセス
2. アカウント作成/ログイン
3. 「API Keys」→「Create new secret key」
4. 生成されたキーをコピー

#### 環境変数への登録（Cloudflare Workers）
```bash
cd relay-server
wrangler secret put GEMINI_API_KEY
wrangler secret put OPENAI_API_KEY
```
または Cloudflareダッシュボードから:
1. Workers & Pages → baketa-relay → Settings → Variables
2. 「Edit Variables」→「Add Variable」でシークレットとして追加

---

## 14. 参考資料

- [Gemini API Documentation](https://ai.google.dev/docs)
- [OpenAI API Documentation](https://platform.openai.com/docs)
- Issue #78: https://github.com/koizumiiiii/Baketa/issues/78
- 元ドキュメント: `C:\Users\suke0\Downloads\AI_mode.md`
