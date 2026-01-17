# Cloudflare Relay Server API使用状況分析

## 概要

BaketaアプリケーションはCloudflare Workers（`baketa-relay.suke009.workers.dev`）をRelay Serverとして使用し、以下の機能を提供しています：

- Cloud AI翻訳（Gemini/OpenAI API呼び出し）
- Patreon OAuth認証・ライセンス管理
- ボーナストークン管理
- 使用量分析
- その他のサーバーサイド処理

**重要**: Cloudflare Workers Free Planには以下の制限があります：
- **100,000リクエスト/日**
- **10ms CPU時間/リクエスト**

本ドキュメントでは、APIの使用状況とリクエスト削減の最適化ポイントを分析します。

---

## エンドポイント一覧

### 高頻度エンドポイント

| エンドポイント | 説明 | 頻度 | インパクト |
|---------------|------|------|----------|
| `/api/translate` | Cloud AI翻訳 | 秒〜分単位 | **最大** |
| `/api/analytics/events` | 使用量分析 | 5分ごと | 中 |
| `/api/auth/refresh` | JWTリフレッシュ | 1時間ごと | 低 |

### 定期バックグラウンドエンドポイント

| エンドポイント | 説明 | 頻度 | インパクト |
|---------------|------|------|----------|
| `/api/patreon/license-status` | ライセンス状態取得 | 30分ごと | 低 |
| `/api/bonus-tokens/sync` | ボーナストークン同期 | 30分ごと | 低 |
| `/api/quota/status` | クォータ状態確認 | 30分ごと | 低 |
| `/api/consent/status` | 同意状態確認 | 24時間ごと | 極低 |

### ユーザー操作依存エンドポイント

| エンドポイント | 説明 | タイミング | インパクト |
|---------------|------|-----------|----------|
| `/api/patreon/exchange` | Patreon認証 | 初回認証時 | 極低 |
| `/api/patreon/revoke` | セッション無効化 | 連携解除時 | 極低 |
| `/api/promotion/redeem` | プロモコード適用 | コード入力時 | 極低 |
| `/api/consent/record` | 同意記録 | 初回同意時 | 極低 |
| `/api/crash-report` | クラッシュレポート | エラー発生時 | 極低 |

---

## 詳細分析

### 1. `/api/translate` - Cloud AI翻訳

**最も頻繁に呼び出されるエンドポイント**

```
呼び出し元: RelayServerClient.TranslateImageAsync()
認証方式: Bearer Token (Patreon Session / Supabase JWT)
```

**呼び出しタイミング**:
- Live翻訳モード: 画像変化検出時（数秒〜数分間隔）
- Singleshot翻訳モード: 手動トリガー時

**リクエスト頻度の見積もり**:
| シナリオ | 1時間あたり | 1日あたり（8時間使用） |
|---------|------------|---------------------|
| Live翻訳（10秒間隔） | 360 | 2,880 |
| Live翻訳（30秒間隔） | 120 | 960 |
| Singleshot（手動） | 10-30 | 80-240 |

**最適化ポイント**:
1. 画像変化検出の閾値を調整して不要なリクエストを削減
2. クライアントサイドキャッシュ（同一画像の再翻訳防止）
3. リクエストのデバウンス

---

### 2. `/api/analytics/events` - 使用量分析

```
呼び出し元: UsageAnalyticsService.FlushAsync()
認証方式: X-Analytics-Key ヘッダー
```

**呼び出しタイミング**:
- 5分ごとの定期フラッシュ
- バッファが50件に達した時
- アプリ終了時

**条件**:
- `IPrivacyConsentService.HasUsageStatisticsConsent` = true の場合のみ
- DEBUGビルドでは `Analytics:EnableInDebug` = true でないと無効

**1日あたりの見積もり**:
```
定期フラッシュ: 12回/時 × 8時間 = 96リクエスト
```

---

### 3. `/api/patreon/license-status` - ライセンス状態取得

```
呼び出し元: PatreonOAuthService.GetLicenseStatusAsync()
認証方式: Bearer Token (Patreon Session)
```

**呼び出しタイミング**:
- ログイン時: 1回
- BonusSyncHostedService: 30分ごと

**1日あたりの見積もり**:
```
定期同期: 2回/時 × 24時間 = 48リクエスト
```

---

### 4. `/api/bonus-tokens/status` & `/api/bonus-tokens/sync`

```
呼び出し元: BonusTokenService
認証方式: Bearer Token (Supabase JWT)
```

**呼び出しタイミング**:
- `/status`: ログイン時、起動時
- `/sync`: 30分ごと（未同期の消費がある場合）、ログアウト時

**1日あたりの見積もり**:
```
/status: 1-2回
/sync: 最大48回（消費がある場合）
```

---

### 5. `/api/quota/status` - クォータ状態確認

```
呼び出し元: RelayServerClient.GetQuotaStatusAsync()
認証方式: Bearer Token
```

**呼び出しタイミング**:
- 起動時: 1回
- BonusSyncHostedService: 30分ごと

**Note**: Issue #296では、翻訳レスポンスに `monthly_usage` が含まれるため、
追加の `/api/quota/status` 呼び出しは冗長になる可能性があります。

---

### 6. `/api/auth/token` & `/api/auth/refresh` - JWT認証

```
呼び出し元: JwtTokenService
```

**呼び出しタイミング**:
- `/token`: Patreon認証直後（1回）
- `/refresh`: JWT有効期限の120秒前

**JWT有効期限**: 1時間（デフォルト）

**1日あたりの見積もり**:
```
/refresh: 8-24回（アクティブセッション時間に依存）
```

---

## リクエスト数見積もり（典型的な1日）

### シナリオ: 一般ユーザー（8時間使用、Live翻訳30秒間隔）

| エンドポイント | リクエスト数 |
|---------------|------------|
| `/api/translate` | 960 |
| `/api/analytics/events` | 96 |
| `/api/patreon/license-status` | 48 |
| `/api/bonus-tokens/sync` | 16 |
| `/api/quota/status` | 48 |
| `/api/auth/refresh` | 8 |
| その他（初回・低頻度） | 10 |
| **合計** | **約1,186** |

### シナリオ: ヘビーユーザー（8時間使用、Live翻訳10秒間隔）

| エンドポイント | リクエスト数 |
|---------------|------------|
| `/api/translate` | 2,880 |
| その他 | 226 |
| **合計** | **約3,106** |

### Cloudflare Free Plan上限との比較

```
1日の上限: 100,000リクエスト

一般ユーザー: 100,000 ÷ 1,186 ≒ 84ユーザー/日
ヘビーユーザー: 100,000 ÷ 3,106 ≒ 32ユーザー/日
```

---

## 最適化提案

### 1. 翻訳リクエストの削減（最重要）

| 施策 | 効果 |
|------|------|
| 画像変化検出閾値の調整 | 30-50%削減 |
| クライアントサイドキャッシュ | 10-30%削減 |
| デバウンス（500ms） | 10-20%削減 |
| バッチ翻訳（複数テキスト一括） | 該当時大幅削減 |

### 2. 定期同期の統合・削減

**現状**:
- `/api/patreon/license-status`: 30分ごと
- `/api/bonus-tokens/sync`: 30分ごと
- `/api/quota/status`: 30分ごと

**提案**:
- 単一の `/api/sync/all` エンドポイントに統合
- または同期間隔を60分に延長

**効果**: 定期リクエストを1/3〜1/2に削減

### 3. `/api/quota/status` の呼び出し削減

翻訳レスポンスに `monthly_usage` が含まれるため、
別途 `/api/quota/status` を呼び出す必要性を再検討

### 4. Analytics送信の最適化

**現状**: 5分ごと + 50件バッファ

**提案**:
- 送信間隔を15分に延長
- バッファサイズを100件に拡大

**効果**: 75%削減（96→24リクエスト/日）

### 5. Cloudflare Workers Paid Planへの移行

| プラン | 上限 | 料金 |
|--------|------|------|
| Free | 100,000リクエスト/日 | 無料 |
| Paid | 10,000,000リクエスト/月 | $5/月〜 |

Paid Planでは月333,000リクエスト/日が可能になり、
スケーラビリティの問題が大幅に緩和されます。

---

## 実装ファイル一覧

| サービス | ファイル |
|---------|---------|
| RelayServerClient | `Baketa.Infrastructure/Translation/Cloud/RelayServerClient.cs` |
| PatreonOAuthService | `Baketa.Infrastructure/License/Services/PatreonOAuthService.cs` |
| BonusTokenService | `Baketa.Infrastructure/License/BonusTokenService.cs` |
| PromotionCodeService | `Baketa.Infrastructure/License/PromotionCodeService.cs` |
| JwtTokenService | `Baketa.Infrastructure/Auth/JwtTokenService.cs` |
| ConsentService | `Baketa.Infrastructure/Services/Settings/ConsentService.cs` |
| UsageAnalyticsService | `Baketa.Infrastructure/Analytics/UsageAnalyticsService.cs` |
| CrashReportSender | `Baketa.Infrastructure/CrashReporting/CrashReportSender.cs` |

---

## 関連ドキュメント

- [external-services.md](./external-services.md) - 外部サービス連携概要
- [cloudflare-kv-optimization.md](./cloudflare-kv-optimization.md) - KV最適化

---

## 🚨 重大な問題: wrangler tailログ分析（2026-01-16）

実際のwrangler tailログを分析した結果、**想定の10-20倍のリクエスト**が発生していることが判明しました。

### 問題1: Freeプランのキャッシュ無効化ループ（致命的）

**ファイル**: `relay-server/src/translate.ts:873-878`

```typescript
// [Issue #296] Freeプランのキャッシュはスキップして再認証
// Patreon連携後にキャッシュが古いままの場合があるため
if (cachedAuth.plan === PLAN.FREE) {
  console.log(`[Issue #296] Skipping Free plan cache, re-authenticating...`);
  await deleteAuthCache(cacheKey);  // ← 毎回キャッシュを削除
}
```

**なぜこのロジックを追加したか（経緯）**:
- Issue #296の実装時、Patreon連携直後にAuth Cacheが古いFreeプランのままで、
  有料プランが反映されない問題があった
- 対策として「Freeプランのキャッシュは信用しない」ロジックを追加
- 意図: ユーザーがPatreon連携した直後に、古いFreeキャッシュで翻訳拒否されるのを防ぐ

**問題点**:
```
リクエスト → キャッシュヒット(Free) → キャッシュ削除 → 再認証(Free) → キャッシュ保存
    ↓
次のリクエスト → キャッシュヒット(Free) → キャッシュ削除 → 再認証(Free) → キャッシュ保存
    ↓
無限ループ（キャッシュが一切効かない）
```

**実際のログ証拠**:
```
[Issue #296] Skipping Free plan cache, re-authenticating: userId=9b484908...
[Issue #296] Auth cache deleted: key=auth:v2:db28...
... (認証処理 + Supabase/Patreon API呼び出し) ...
[Issue #286] Auth cached (Cache API): key=auth:v2:db28...
↓ 次のリクエストで同じことが繰り返される
```

**影響**:
- Freeプランユーザーは**全リクエストで再認証**が発生
- 1リクエストあたり3-5倍のAPI呼び出し（Supabase getUser + profiles検索 + Patreon KV確認）
- キャッシュの意味が完全に失われる

### 問題2: 起動時のAPI呼び出し重複（クライアント側）

wrangler tailログで確認された、**起動後3秒間**のAPI呼び出し:

| エンドポイント | 呼び出し回数 | 期待値 |
|---------------|-------------|-------|
| `/api/bonus-tokens/status` | **3回** | 1回 |
| `/api/quota/status` | **3回** | 1回 |
| `/api/promotion/status` | **4回** | 1回 |
| `/api/consent/status` | **5回** | 1回 |

**原因の特定**:

1. **複数のHostedServiceが並列起動**:
   - `AuthInitializationService`: promotion/status, consent/status を呼び出し
   - `BonusSyncHostedService`: bonus-tokens/status, quota/status を呼び出し
   - `PatreonSyncHostedService`: patreon/license-status を呼び出し

2. **イベントハンドラの重複**:
   - `AuthStatusChanged` イベントで複数のサービスが同時に反応
   - 起動時 + ログインイベント で同じAPIが2重呼び出し

3. **重複防止機構の欠如**:
   - 同じAPIへの並列リクエストを抑制する仕組みがない
   - Rate limitingやdebounceが未実装

**ファイル一覧**:
| ファイル | 呼び出すAPI |
|---------|-----------|
| `AuthInitializationService.cs` | promotion/status, consent/status |
| `BonusSyncHostedService.cs` | bonus-tokens/status, quota/status |
| `PatreonSyncHostedService.cs` | patreon/license-status |

### 実際の影響（計算）

**想定（ドキュメント記載）**:
- 起動時: 各エンドポイント1回 = 5リクエスト

**実際（ログ分析）**:
- 起動時: 15-20リクエスト
- 各リクエストでFreeプラン再認証 = 15-20 × 3-5 = **45-100サブリクエスト**

**1日あたりの影響**:
| 項目 | 想定値 | 実際値 | 乗数 |
|------|--------|--------|------|
| 起動1回あたり | 5 | 45-100 | ×9-20 |
| 翻訳1回あたり（Free） | 1 | 3-5 | ×3-5 |
| 1日あたり合計 | 1,186 | 5,000-15,000 | ×4-12 |

---

## 対応方針

### 方針A: サーバー側修正（優先度: 最高）

**Freeプランキャッシュの条件付きスキップ**:

```typescript
// 修正案: Patreon紐づけユーザーのみ再認証
if (cachedAuth.plan === PLAN.FREE && cachedAuth.authMethod === 'supabase') {
  // Supabase認証でFreeプランの場合のみ、Patreon紐づけを確認
  const profile = await getProfile(cachedAuth.userId);
  if (profile?.patreon_user_id) {
    // Patreon紐づけあり → 再認証でプラン確認
    await deleteAuthCache(cacheKey);
  } else {
    // Patreon紐づけなし → キャッシュを信用
    return cachedAuth;
  }
}
```

**効果**:
- 純粋なFreeユーザー（Patreon未連携）はキャッシュが効く
- Patreon連携済みユーザーのみ再認証

### 方針B: クライアント側修正（優先度: 高）

1. **起動時呼び出しの統合**:
   - 複数サービスの初期化を1つの `AppInitializationService` に統合
   - 重複呼び出しを排除

2. **イベントハンドラの整理**:
   - `AuthStatusChanged` で呼び出すAPIを1箇所に集約
   - 重複防止フラグの導入

3. **リクエストのデバウンス**:
   - 同一エンドポイントへの連続リクエストを抑制
   - 最小間隔: 1秒

### 方針C: 統合エンドポイント（優先度: 中）

```
POST /api/sync/all
→ promotion/status + consent/status + bonus-tokens/status + quota/status を1リクエストで取得
```

**効果**: 起動時リクエストを15-20 → 1-2に削減

### 実装優先順位

| 順位 | 対応 | 効果 | 工数 |
|------|------|------|------|
| 1 | Freeプランキャッシュ修正 | ×3-5削減 | 小 |
| 2 | 起動時呼び出し統合 | ×3-4削減 | 中 |
| 3 | 統合エンドポイント | さらに削減 | 大 |

---

## 更新履歴

- 2026-01-16: wrangler tailログ分析結果と対応方針を追加
- 2026-01-16: 初版作成（Issue #296 調査に基づく）
