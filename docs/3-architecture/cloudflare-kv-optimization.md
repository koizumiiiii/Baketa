# Cloudflare Workers KV 最適化設計

## 概要

Relay Server（Cloudflare Workers）で使用しているKVストレージの使用量を最適化し、Free Tier制限内での運用を可能にするための設計ドキュメント。

## Free Tier制限

| リソース | 上限/日 |
|---------|--------|
| Read | 100,000 |
| **Write** | **1,000** ← ボトルネック |

## 現状の設計（Issue #286 対応後）

### 実施済み最適化

| 改善 | 内容 | 効果 |
|------|------|------|
| Phase 1 | キャッシュTTL延長（60秒/5分 → 1時間） | Write 90%削減 |
| Phase 2 | グローバルIPレートリミット削除 | Write 100%削減（該当部分） |
| Phase 3 | 認証キャッシュをCache APIに移行 | Write 100%削減（該当部分） |

### KV操作の種類

#### 1. セッション管理（必須・低頻度）
- **キー形式**: `{sessionToken}`
- **TTL**: 30日
- **操作タイミング**: ログイン時のみ
- **影響**: 極小

#### 2. メンバーシップキャッシュ（最適化済み）
- **キー形式**: `membership:{userId}`
- **TTL**: 1時間（改善前: 5分）
- **操作タイミング**: メンバーシップ確認時
- **影響**: 小

#### 3. 認証キャッシュ（Cache API移行済み）
- **キー形式**: `auth:{tokenHash}`
- **ストレージ**: Cache API（KV制限対象外）
- **TTL**: 1時間
- **影響**: なし

#### 4. エンドポイント単位レートリミット（残存問題）
- **キー形式**: `ratelimit:{endpoint}:{identifier}`
- **TTL**: 2分
- **操作タイミング**: 各エンドポイントへのアクセス時
- **影響**: 中〜大（後述）

## 残存する問題点

### 問題1: バックグラウンドサービスによるKV消費

アプリを起動しているだけで、以下のバックグラウンドサービスが定期的にAPIを呼び出す：

| サービス | 間隔 | 呼び出すAPI | KV操作/回 |
|---------|------|-----------|----------|
| PatreonSyncHostedService | 30分 | `/api/membership`, `/api/patreon/license-status` | 2-4 |
| BonusSyncHostedService | 30分 | `/api/bonus-tokens/status`, `/api/bonus-tokens/sync` | 2-4 |

**1ユーザー・1日あたりの消費:**
- 30分ごとに約2回のAPI呼び出し × 48回/日 = 96回/日
- エンドポイント単位レートリミット: 2 KV ops × 96 = **約192 KV Write/日**

**スケーラビリティ:**
- Free Tier (1,000 Write/日) で対応可能なユーザー数: **約5ユーザー**
- 翻訳使用を含めると: **約3ユーザー**

### 問題2: エンドポイント単位レートリミットの非効率性

各エンドポイントが個別にレートリミットを実装：

```typescript
// 現状: 各エンドポイントで個別にKV操作
const rateLimit = await checkRateLimit(env, `bonus-status:${user.id}`, 10);
const rateLimit = await checkRateLimit(env, `bonus-sync:${user.id}`, 30);
// ...
```

**問題点:**
- 各リクエストでGET + PUT = 2 KV操作
- 同一ユーザーでも異なるキーで重複管理
- レートリミットの必要性が低いエンドポイントにも適用

## 改善提案（将来対応）

### 提案1: バックグラウンド同期間隔の延長

**現状:** 30分間隔
**提案:** 1〜2時間間隔

```csharp
// PatreonSyncHostedService.cs
private static readonly TimeSpan SyncInterval = TimeSpan.FromHours(1); // 30分 → 1時間
```

**効果:** KV消費を50%削減
**リスク:** ライセンス状態の反映が遅れる（手動同期で対応可能）

### 提案2: エンドポイント単位レートリミットをCache APIに移行

認証キャッシュと同様に、レートリミットもCache APIを使用：

```typescript
// 提案: Cache APIベースのレートリミット
async function checkRateLimitWithCache(identifier: string, maxRequests: number): Promise<boolean> {
  const cache = caches.default;
  const cacheKey = new Request(`https://ratelimit.internal/${identifier}`);
  // ...
}
```

**効果:** レートリミットによるKV消費を100%削減
**注意:** Cache APIは分散環境で正確性が保証されない（許容可能）

### 提案3: レートリミット対象エンドポイントの見直し

**現状のレートリミット対象:**
| エンドポイント | 制限 | 必要性 |
|--------------|------|-------|
| `/api/bonus-tokens/status` | 10/分 | 低（読み取りのみ） |
| `/api/bonus-tokens/sync` | 30/分 | 中（書き込みあり） |
| `/api/consent/status` | 10/分 | 低（読み取りのみ） |
| `/api/promotion/status` | 10/分 | 低（読み取りのみ） |
| `/api/analytics` | 120/分 | 中 |
| `/patreon/webhook` | 100/分 | 高（外部からの呼び出し） |

**提案:**
- 読み取り専用エンドポイントのレートリミットを削除または緩和
- Webhookは維持（外部からの攻撃対策）

### 提案4: Cloudflare WAF Rate Limiting Rules

コードレベルではなく、Cloudflareダッシュボードでレートリミットを設定：

**設定場所:** Dashboard → Security → WAF → Rate limiting rules

**推奨ルール:**
```
Rule 1: All endpoints
- If: Request rate > 60 requests per 1 minute
- Then: Block for 1 minute
- Scope: IP address
```

**効果:** KV操作を完全に排除しつつDoS対策を維持

## 優先度付きアクションアイテム

| 優先度 | 項目 | 効果 | 実装難易度 |
|-------|------|------|-----------|
| 高 | WAF Rate Limiting設定 | KV 100%削減 | 低（設定のみ） |
| 中 | 同期間隔延長 | KV 50%削減 | 低 |
| 中 | レートリミットCache API移行 | KV 100%削減 | 中 |
| 低 | レートリミット対象見直し | KV 30%削減 | 低 |

## モニタリング

### Cloudflareダッシュボードでの確認

1. **Workers & Pages** → **baketa-relay** → **Metrics**
2. **KV** → **SESSIONS** → **Usage**

### 確認すべき指標

- Daily Writes: 1,000未満であること
- Daily Reads: 100,000未満であること
- Peak時間帯のWrite数

## Issue #296: トークン消費追跡の最適化設計

### 設計方針

**原則: 新規Cloudflare Workers呼び出しを追加しない**

トークン消費追跡はサーバーサイドで管理するが、Cloudflare Workers への呼び出し回数は増やさない設計とする。

### 実装アプローチ

#### 1. トークン記録（Relay Server側）

```
翻訳リクエスト → Gemini API → 成功 → Supabase RPC呼び出し → レスポンス返却
                                     ↑
                           record_token_consumption
                           (追加のWorker呼び出しなし)
```

**ポイント:**
- 翻訳成功時に同一リクエスト内でSupabase RPCを呼び出し
- クライアントからの追加API呼び出しは不要
- Supabase呼び出しはCloudflare Workers制限とは別カウント

#### 2. トークン使用量の取得（クライアント側）

**方法A: 翻訳レスポンスに含める（推奨）**
```json
{
  "translation": "...",
  "tokenUsage": {
    "thisRequest": 1234,
    "monthlyUsed": 50000,
    "monthlyLimit": 10000000
  }
}
```
- 追加API呼び出し: **0回**
- UI更新: 翻訳ごとに最新値を表示

**方法B: 起動時に1回取得**
```
アプリ起動 → GET /api/token-usage → ローカルキャッシュ更新
```
- 追加API呼び出し: **1回/起動**
- 翻訳レスポンスの方法Aと併用

#### 3. 避けるべきパターン

❌ **定期ポーリング**
```csharp
// 絶対にやらない
while (true) {
    await GetTokenUsageAsync(); // 30分ごと
    await Task.Delay(TimeSpan.FromMinutes(30));
}
```

❌ **翻訳前の残量チェック**
```csharp
// やらない - サーバーが上限チェックする
if (await GetRemainingTokensAsync() <= 0) return;
```

### Cloudflare Workers への影響

| 操作 | 既存 | Issue #296後 | 増加 |
|------|------|-------------|------|
| 翻訳API呼び出し | 1回/翻訳 | 1回/翻訳 | **0** |
| トークン取得 | N/A | レスポンスに含む | **0** |
| 起動時同期 | N/A | 1回/起動（オプション） | **+1** |

### 実装優先度

1. **Phase 1**: Relay Server で翻訳成功時にSupabase RPC呼び出し
2. **Phase 2**: 翻訳レスポンスに `tokenUsage` フィールド追加
3. **Phase 3**: クライアントUI更新（レスポンスから表示）

## 関連Issue

- [Issue #286](https://github.com/koizumiiiii/Baketa/issues/286): Cloudflare Workers KV操作の最適化（Free Tier制限対策）
- [Issue #296](https://github.com/koizumiiiii/Baketa/issues/296): サーバーサイドトークン消費追跡

## 更新履歴

| 日付 | 内容 |
|------|------|
| 2026-01-13 | 初版作成（Issue #286対応完了後） |
| 2026-01-14 | Issue #296 トークン消費追跡の最適化設計追加 |
