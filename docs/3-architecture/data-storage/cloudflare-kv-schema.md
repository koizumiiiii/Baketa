# Cloudflare KV スキーマ

> 最終更新: 2026-01-18

## 概要

BaketaはCloudflare KVをセッション管理とキャッシュに使用しています。

- **Namespace**: `SESSIONS` (ID: `7fdcdf963f8649578fe1ccaadaa74ea8`)
- **用途**: Patreonセッション管理、メンバーシップキャッシュ、リフレッシュトークン

---

## キーパターン一覧

| キーパターン | 用途 | TTL | データ型 |
|-------------|------|-----|---------|
| `{sessionToken}` | Patreonセッション | 30日 | `SessionData` |
| `usertoken:{patreonUserId}` | ユーザートークン集約 | 31日 | `UserTokenData` |
| `membership:{patreonUserId}` | メンバーシップキャッシュ | 1時間 | `CachedMembership` |
| `refresh:{refreshToken}` | JWTリフレッシュトークン | 30日 | `RefreshTokenData` |
| `crashreport:ratelimit:{ip}` | クラッシュレポートレート制限 | 60秒 | `RateLimitData` |

---

## データ構造詳細

### SessionData

Patreon OAuth認証後のセッション情報。

```typescript
interface SessionData {
  accessToken: string;      // Patreon Access Token
  refreshToken: string;     // Patreon Refresh Token
  expiresAt: number;        // トークン有効期限 (Unix timestamp ms)
  userId: string;           // Patreon User ID (数字文字列)
}
```

**キー形式**: `{sessionToken}` (UUID形式)
**TTL**: 30日 (`SESSION_TTL_SECONDS = 30 * 24 * 60 * 60`)

**用途**:
- クライアントからのAPI認証
- Patreon API呼び出し用トークン保持

---

### UserTokenData

ユーザー単位のトークン集約（シングルデバイス強制用）。

```typescript
interface UserTokenData {
  accessToken: string;       // Patreon Access Token
  refreshToken: string;      // Patreon Refresh Token
  expiresAt: number;         // トークン有効期限
  email: string;             // Patreonメールアドレス
  fullName: string;          // Patreon表示名
  sessionTokens: string[];   // 常に1要素のみ（シングルデバイス強制）
  updatedAt: number;         // 更新日時
}
```

**キー形式**: `usertoken:{patreonUserId}`
**TTL**: 31日 (`USER_TOKEN_TTL_SECONDS = 31 * 24 * 60 * 60`)

**用途**:
- 新規ログイン時に既存セッションを無効化
- 1ユーザー1デバイス強制

---

### CachedMembership

Patreonメンバーシップ情報のキャッシュ。

```typescript
interface CachedMembership {
  membership: ParsedMembership;
  cachedAt: number;          // キャッシュ作成日時
}

interface ParsedMembership {
  userId: string;            // Patreon User ID
  email: string;
  fullName: string;
  plan: 'Free' | 'Pro' | 'Premium' | 'Ultimate';
  hasBonusTokens: boolean;
  pledgeAmountCents: number;
  campaignLifetimeSupportCents: number;
  nextChargeDate?: string;
  isActive: boolean;
}
```

**キー形式**: `membership:{patreonUserId}`
**TTL**: 1時間 (`IDENTITY_CACHE_TTL_SECONDS = 60 * 60`)

**用途**:
- Patreon API呼び出し削減
- プラン判定の高速化
- 手動同期で即時更新可能

---

### RefreshTokenData

Issue #287 JWT認証用のリフレッシュトークン。

```typescript
interface RefreshTokenData {
  userId: string;            // Patreon User ID
  sessionToken: string;      // 元のセッショントークン
  createdAt: number;
  expiresAt: number;
  isUsed: boolean;           // 1回使用で無効化
}
```

**キー形式**: `refresh:{refreshToken}`
**TTL**: 30日 (`JWT_REFRESH_TOKEN_TTL_SECONDS = 30 * 24 * 60 * 60`)

**用途**:
- JWTアクセストークンの更新
- リプレイ攻撃防止（1回使用制限）

---

### RateLimitData

レートリミット用カウンター（一部はCache APIに移行済み）。

```typescript
interface RateLimitData {
  count: number;             // リクエスト数
  windowStart: number;       // ウィンドウ開始時刻
}
```

**キー形式**: `crashreport:ratelimit:{clientIP}`
**TTL**: 60秒 (`RATE_LIMIT_WINDOW_SECONDS = 60`)

---

## Cache API 使用箇所

Issue #299でKV Writeコスト削減のため、一部のデータをCache APIに移行。

| データ種別 | URL形式 | TTL |
|-----------|---------|-----|
| レートリミット | `https://baketa-relay.../ratelimit/{identifier}/{windowStart}` | 120秒 |
| クォータキャッシュ | `https://baketa-relay.../cache/quota:{userId}:{yearMonth}` | 5分 |
| 認証キャッシュ | `https://baketa-relay.../cache/auth:{sessionToken}` | 60秒 |

---

## セッションライフサイクル

```
[Patreon OAuth 認証フロー]

1. ユーザーがPatreon認証開始
   └─→ Patreon OAuth画面へリダイレクト

2. コールバック受信
   └─→ POST /api/auth/callback
       ├─→ Patreon API: トークン交換
       ├─→ Patreon API: ユーザー情報取得
       ├─→ KV: SessionData 保存 (key: {sessionToken})
       ├─→ KV: UserTokenData 更新 (key: usertoken:{userId})
       │    └─→ 既存セッションを無効化（シングルデバイス強制）
       └─→ KV: CachedMembership 保存 (key: membership:{userId})

3. API認証（各リクエスト）
   └─→ Authorization: Bearer {sessionToken}
       ├─→ KV: SessionData 取得
       ├─→ KV: CachedMembership 取得（期限切れならPatreon API呼び出し）
       └─→ 認証成功 / 失敗

4. セッション更新
   └─→ POST /api/auth/refresh
       ├─→ Patreon API: リフレッシュトークン使用
       ├─→ KV: SessionData 更新
       └─→ KV: UserTokenData 更新

5. ログアウト
   └─→ POST /api/auth/logout
       ├─→ KV: SessionData 削除
       ├─→ KV: UserTokenData 削除
       └─→ KV: CachedMembership 削除
```

---

## Supabaseとの関係

```
┌─────────────────────────────────────────────────────────────┐
│                    Cloudflare KV                             │
│  ┌─────────────────┐    ┌─────────────────────────────┐     │
│  │ SessionData     │    │ CachedMembership            │     │
│  │ userId: "123"   │───▶│ userId: "123" (Patreon ID)  │     │
│  │ (Patreon ID)    │    │ plan: "Pro"                 │     │
│  └─────────────────┘    └─────────────────────────────┘     │
└─────────────────────────────────┬───────────────────────────┘
                                  │
                                  │ patreon_user_id
                                  ▼
┌─────────────────────────────────────────────────────────────┐
│                      Supabase                                │
│  ┌─────────────────────────────────────────────────┐        │
│  │ profiles                                         │        │
│  │ id: "673debd1-..." (Supabase UUID)              │        │
│  │ patreon_user_id: "123" ←── KV userId            │        │
│  └──────────────────────────┬──────────────────────┘        │
│                             │                                │
│      ┌──────────────────────┼──────────────────────┐        │
│      ▼                      ▼                      ▼        │
│  ┌────────────┐    ┌────────────────┐    ┌──────────────┐  │
│  │usage_events│    │token_usage     │    │bonus_tokens  │  │
│  │user_id     │    │user_id         │    │user_id       │  │
│  └────────────┘    └────────────────┘    └──────────────┘  │
└─────────────────────────────────────────────────────────────┘
```

**重要**: KVの`userId`は**Patreon User ID**（数字文字列）、
Supabaseの`user_id`は**Supabase UUID**。
`profiles.patreon_user_id`で紐付け。

---

## 環境変数 (Secrets)

| 名前 | 説明 | 設定方法 |
|------|------|---------|
| `PATREON_CLIENT_ID` | Patreon OAuth Client ID | Dashboard |
| `PATREON_CLIENT_SECRET` | Patreon OAuth Client Secret | `wrangler secret` |
| `PATREON_WEBHOOK_SECRET` | Webhook署名検証用 | `wrangler secret` |
| `GEMINI_API_KEY` | Cloud AI翻訳用 | `wrangler secret` |
| `SUPABASE_URL` | Supabase URL | `wrangler secret` |
| `SUPABASE_SERVICE_KEY` | Supabase Service Key | `wrangler secret` |
| `JWT_SECRET` | JWT署名用 (Issue #287) | `wrangler secret` |

---

## 関連ドキュメント

- [Supabase スキーマ](./supabase-schema.md)
- [認証フロー](../auth/authentication-flow.md)
- [外部サービス連携](../external-services.md)
