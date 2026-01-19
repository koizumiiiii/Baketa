# データフロー分析と不整合調査

> 最終更新: 2026-01-18
> ステータス: 調査中

## 概要

BaketaのバックエンドはCloudflare Workers (Relay Server) と Supabase の2層構成。
本ドキュメントでは、データの流れ、ID体系の対応関係、および潜在的な不整合を分析する。

---

## ID体系

### 3種類のユーザーID

| ID種別 | 形式 | 発行元 | 用途 |
|--------|------|--------|------|
| **Supabase UUID** | `673debd1-a20e-42bb-8e4c-1cda317e75d1` | Supabase Auth | DB主キー、API認証 |
| **Patreon User ID** | `197440691` | Patreon | 課金管理、KVセッション |
| **Session Token** | `abc123...` (UUID) | Relay Server | API認証 (Bearer) |

### ID間の対応関係

```
┌─────────────────────────────────────────────────────────────┐
│                     クライアント                             │
│  ┌─────────────────────────────────────────────────────┐   │
│  │ LicenseManager.CurrentSessionId                      │   │
│  │ → Patreon KV Session Token (UUID)                   │   │
│  │ → または Supabase JWT (eyJ...)                       │   │
│  └─────────────────────────────────────────────────────┘   │
└────────────────────────────┬────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────┐
│                   Relay Server 認証                          │
│                                                              │
│  認証フローバック（優先順）:                                   │
│  1. Relay Server JWT検証 → sub クレーム取得                  │
│  2. Patreon KV Session → patreonUserId 取得                 │
│     └─→ profiles.patreon_user_id → profiles.id (UUID)      │
│  3. Supabase JWT → auth.users.id (UUID)                     │
│                                                              │
└─────────────────────────────────────────────────────────────┘
```

---

## 機能別データフロー

### 1. Patreon認証

```
[クライアント]
    │ 1. Patreon OAuth開始
    ▼
[Patreon]
    │ 2. OAuth callback
    ▼
[Relay Server: /api/auth/callback]
    │ 3. トークン交換、ユーザー情報取得
    │ 4. KV保存:
    │    - SessionData (key: sessionToken)
    │    - UserTokenData (key: usertoken:{patreonUserId})
    │    - CachedMembership (key: membership:{patreonUserId})
    │ 5. Supabase連携:
    │    - profiles.patreon_user_id 更新 (link_patreon_user RPC)
    ▼
[クライアント]
    │ 6. sessionToken 保存
    │ 7. API呼び出し時: Authorization: Bearer {sessionToken}
```

**保存されるデータ**:
| 保存先 | キー/テーブル | データ |
|--------|-------------|--------|
| KV | `{sessionToken}` | SessionData (patreonUserId含む) |
| KV | `usertoken:{patreonUserId}` | UserTokenData |
| KV | `membership:{patreonUserId}` | CachedMembership (plan含む) |
| Supabase | `profiles` | patreon_user_id を設定 |

---

### 2. Supabase認証 (Google/Discord/Twitch)

```
[クライアント]
    │ 1. Supabase OAuth開始
    ▼
[Supabase Auth]
    │ 2. OAuth処理
    │ 3. JWT発行
    │ 4. auth.users レコード作成
    │ 5. profiles レコード作成 (トリガー)
    ▼
[クライアント]
    │ 6. Supabase JWT 保存
    │ 7. API呼び出し時: Authorization: Bearer {jwt}
```

**保存されるデータ**:
| 保存先 | キー/テーブル | データ |
|--------|-------------|--------|
| Supabase | `auth.users` | id (UUID), email |
| Supabase | `profiles` | id = auth.users.id, patreon_user_id = NULL |

**⚠️ 問題点**: Patreon連携なしの場合、`patreon_user_id = NULL`

---

### 3. Cloud AI翻訳

```
[クライアント]
    │ POST /api/translate
    │ Authorization: Bearer {token}
    ▼
[Relay Server: translate.ts]
    │ 1. 認証（authenticateUser関数）
    │    ├─ Relay Server JWT検証
    │    ├─ Patreon KVセッション検証
    │    └─ Supabase JWT検証
    │ 2. プラン判定
    │ 3. トークン使用量チェック（Supabase token_usage）
    │ 4. Cloud AI (Gemini) 呼び出し
    │ 5. トークン使用量更新（Supabase token_usage）
    ▼
[クライアント]
    │ 翻訳結果受信
```

**認証フロー詳細** (`authenticateUser`):
```typescript
// 1. Relay Server JWT
const jwtClaims = await validateJwtToken(env, token);
if (jwtClaims) return { userId: jwtClaims.sub, authType: 'jwt' };

// 2. Patreon KV Session
const session = await getSession(env, token);
if (session && !expired) return { userId: session.userId, authType: 'patreon' };

// 3. Supabase JWT
const { user } = await supabase.auth.getUser(token);
if (user) return { userId: user.id, authType: 'supabase' };
```

---

### 4. 使用統計 (Analytics)

```
[クライアント: UsageAnalyticsService]
    │ TrackEvent("translation", {...})
    │ FlushAsync()
    │ POST /api/analytics/events
    │ Authorization: Bearer {sessionToken}
    ▼
[Relay Server: handleAnalyticsEvents]
    │ 1. 認証（3段階フォールバック）
    │    ├─ Relay Server JWT → userId (sub)
    │    ├─ Patreon KV → patreonUserId → profiles.id
    │    └─ Supabase JWT → auth.users.id
    │ 2. usage_events INSERT
    ▼
[Supabase: usage_events]
    │ user_id = Supabase UUID (profiles.id)
```

**⚠️ 現在の問題**:
- クライアントが `_licenseInfoProvider.CurrentSessionId` を送信
- これがPatreon KVトークンの場合、KVにセッションがなければ失敗
- Supabase JWTの場合は形式チェック（3セグメント）で判定

---

### 5. プロモーションコード

```
[クライアント]
    │ POST /api/promotion/redeem
    │ Authorization: Bearer {supabaseJwt}
    │ Body: { code: "BAKETA-XXXX-XXXX" }
    ▼
[Relay Server: handlePromotionRedeem]
    │ 1. Supabase JWT認証
    │ 2. RPC: redeem_promotion_code
    │    └─ promotion_code_redemptions INSERT (user_id = Supabase UUID)
    │ 3. RPC: grant_bonus_tokens
    │    └─ bonus_tokens INSERT (user_id = Supabase UUID)
    ▼
[Supabase]
    │ promotion_codes.current_uses++
    │ promotion_code_redemptions 作成
    │ bonus_tokens 作成
```

---

## 発見された不整合

### 🔴 重大度: 高

#### 1. KVセッションデータの欠損（確認済み）

**問題**:
- Patreon認証ユーザー（ID: 199724693）のKVデータが存在しない
- `usertoken:199724693` → 404 Not Found
- `membership:199724693` → Error

**根本原因の仮説**:
1. **KV書き込み失敗**: OAuth callback時のKV保存が失敗している
2. **TTL期限切れ**: 30日TTLが切れ、リフレッシュされていない
3. **シングルデバイス強制**: 他デバイスでのログインにより古いセッションが削除された
4. **Webhook未処理**: Patreon Webhookによるデータ更新が失敗

**影響**:
- Analytics API認証が「Invalid or expired session」エラー
- Cloud AI翻訳のプラン判定が失敗
- トークン使用量の追跡が不可能

**対応案**:
1. **即時**: Relay Serverのログを確認し、KV書き込みエラーを特定
2. **短期**: KV書き込み失敗時のエラーハンドリング強化
3. **中期**: KVデータのヘルスチェック機能追加

#### 2. セッショントークンの不一致

**問題**:
- クライアントの `LicenseManager.CurrentSessionId` が返すトークンの種類が一貫していない
- Patreon認証時: KVセッショントークン（UUID形式）
- Supabase認証時: どのトークンが保存されているか不明確

**影響**:
- Analytics API が "Invalid or expired session" エラーを返す
- 認証フォールバックが正しく動作しない可能性

**調査ポイント**:
```csharp
// LicenseManager.cs
public string? CurrentSessionId => CurrentState.SessionId;
// ↑ これが何を返すか？ Patreon KVトークン? Supabase JWT?
```

**対応案**:
1. `BonusSyncHostedService.UpdateSessionTokenAsync()` の優先順位を見直し
2. トークン種別をログ出力して動作確認
3. Issue #287 JWT統一認証システムの実装を加速

#### 3. profiles.patreon_user_id が NULL のユーザー

**問題**:
- Supabase認証（Google/Discord/Twitch）のみのユーザーは `patreon_user_id = NULL`
- Analytics認証フローでPatreon KV → profiles変換が失敗

**影響**:
- Supabase認証ユーザーのAnalyticsが記録されない可能性

**対応案**:
- Supabase JWT認証を直接使用するよう修正
- クライアント側でSupabase JWTを保持・送信

---

### 🟡 重大度: 中

#### 3. 二重定義されたインターフェース

**問題**:
- `SessionData`, `CachedMembership`, `UserTokenData` が複数ファイルで定義
- `index.ts` と `translate.ts` で同一インターフェースが重複

**影響**:
- メンテナンス性の低下
- 将来的な不整合リスク

**対応案**:
- 共通の `types.ts` に統合

#### 4. KV vs Supabase のデータ重複

**問題**:
- メンバーシップ情報が KV (CachedMembership) と Supabase (profiles周辺) に分散
- プラン情報が KV キャッシュ依存

**影響**:
- データの一貫性維持が困難
- キャッシュ期限切れ時の動作が複雑

---

### 🟢 重大度: 低

#### 5. license_history の user_id/patreon_user_id 両方 NULL 許可

**問題**:
```sql
license_history.user_id UUID NULL
license_history.patreon_user_id TEXT NULL
```
- どちらか一方は必須のはずだが、両方NULLが許可されている

**対応案**:
- CHECK制約追加: `CHECK (user_id IS NOT NULL OR patreon_user_id IS NOT NULL)`

#### 6. crash_reports に user_id がない

**問題**:
- プライバシー保護のため意図的だが、デバッグ時に特定ユーザーの問題追跡が困難

**対応案**:
- オプトイン方式でuser_id含める（ユーザー同意時のみ）

---

## 推奨アクション

### 即時対応（Issue #307関連）

1. **クライアント認証トークンの明確化**
   - `LicenseManager.CurrentSessionId` が返す値を調査
   - Supabase JWT を直接使用するオプションを検討

2. **Analytics認証の改善**
   - Supabase JWT認証パスのデバッグ強化
   - トークン形式ログ出力追加

### 中期対応

3. **型定義の統合**
   - `relay-server/src/types.ts` に共通型を集約

4. **データベーススキーマ改善**
   - `license_history` のCHECK制約追加
   - インデックス最適化

### 長期対応

5. **認証システムの統一 (Issue #287)**
   - Relay Server JWT を全APIで使用
   - Patreon KV / Supabase JWT の段階的廃止

---

## UltraThink分析: 追加の潜在的問題

### 🔍 分析観点

1. **データの一貫性**: 同じ情報が複数箇所に保存されていないか
2. **依存関係の明確性**: どのデータがどのデータに依存しているか
3. **障害耐性**: 一部のデータが欠損した場合の影響
4. **スケーラビリティ**: ユーザー数増加時の問題

---

### 🔶 追加で発見した問題

#### A. トークン使用量の二重管理

**問題箇所**:
```
KV: CachedMembership.hasBonusTokens (boolean)
Supabase: bonus_tokens テーブル (詳細データ)
Supabase: token_usage テーブル (消費量)
```

**不整合リスク**:
- KVの `hasBonusTokens` がキャッシュ期限切れ（1時間）で古い情報を返す
- Supabase側でボーナストークンが付与されても、KVキャッシュ更新まで反映されない
- トークン残量判定が不正確になる可能性

**対応案**:
1. ボーナストークン付与時にKVキャッシュを即座に無効化
2. `hasBonusTokens` をSupabase APIから直接取得するオプション追加
3. キャッシュTTLを短縮（1時間 → 15分）

#### B. 認証状態の分散

**問題箇所**:
```
クライアント:
  - LicenseManager.CurrentState.SessionId
  - PatreonCredentials (ローカルファイル)
  - Supabase Session (メモリ)

サーバー:
  - KV SessionData
  - KV UserTokenData
  - Supabase auth.sessions
  - Supabase auth.refresh_tokens
```

**不整合リスク**:
- クライアントとサーバーの認証状態が同期されない
- ログアウト時に一部のセッションデータが残留
- 複数デバイス間でのセッション管理が複雑

**対応案**:
1. セッション状態のSingle Source of Truth（SSoT）を定義
2. ログアウト時の完全クリーンアップ処理を実装
3. Issue #287 JWT統一認証で認証フローを簡素化

#### C. usage_events の user_id 欠損リスク

**問題箇所**:
```sql
usage_events.user_id UUID NULL  -- オプショナル
```

**現状の動作**:
- 認証に失敗した場合、`user_id = NULL` でイベントが保存される可能性
- または、認証エラー時はイベント自体が破棄される

**不整合リスク**:
- ユーザー別の使用統計が不完全になる
- 匿名イベントと認証済みイベントの混在で分析が困難

**対応案**:
1. 認証失敗時も匿名イベントとして保存（プライバシー配慮）
2. `user_id` が NULL のイベントを後から紐付けする仕組み
3. クライアント側でローカルにイベントをバッファリングし、認証成功後に一括送信

#### D. Patreonプラン変更の伝播遅延

**問題箇所**:
```
Patreon (プラン変更)
    ↓ Webhook (リアルタイム)
Relay Server
    ↓ KV更新
CachedMembership (1時間キャッシュ)
    ↓ 最大1時間遅延
クライアント (プラン反映)
```

**不整合リスク**:
- ユーザーがダウングレードしても1時間は旧プラン機能が使える
- アップグレード時も1時間は新機能が使えない

**対応案**:
1. Webhook受信時にクライアントへプッシュ通知（WebSocket/SSE）
2. 手動同期ボタンでキャッシュを即座にリフレッシュ（既存）
3. プラン変更時のみキャッシュTTLを短縮

#### E. crash_reports と usage_events の関連付け不可

**問題箇所**:
```sql
crash_reports: user_id なし（プライバシー保護）
usage_events: user_id あり
```

**分析上の問題**:
- 特定ユーザーのクラッシュ傾向を分析できない
- 使用パターンとクラッシュの相関分析が不可能
- サポート時にユーザーの問題を特定しにくい

**対応案**:
1. オプトイン方式で `user_id` を含める（ユーザー同意時のみ）
2. クラッシュレポートに `session_id` を追加（usage_events と紐付け可能）
3. サポートチケット番号で手動紐付け

---

### 📋 優先度別対応ロードマップ

#### Phase 1: 即時対応（Issue #307関連）

| 問題 | 対応 | 担当 |
|------|------|------|
| KVセッションデータ欠損 | ログ調査、エラーハンドリング強化 | Backend |
| セッショントークン不一致 | ログ出力追加、動作確認 | Client |

#### Phase 2: 短期対応（1-2週間）

| 問題 | 対応 | 担当 |
|------|------|------|
| トークン使用量の二重管理 | キャッシュ無効化ロジック追加 | Backend |
| usage_events user_id欠損 | フォールバック保存実装 | Backend |

#### Phase 3: 中期対応（Issue #287）

| 問題 | 対応 | 担当 |
|------|------|------|
| 認証状態の分散 | JWT統一認証システム実装 | Full Stack |
| Patreon KV依存の解消 | Supabase中心アーキテクチャへ移行 | Full Stack |

---

## 関連ドキュメント

- [Supabase スキーマ](./supabase-schema.md)
- [Cloudflare KV スキーマ](./cloudflare-kv-schema.md)
- [Issue #287: JWT短期トークン認証システム](../../issues/287-jwt-authentication.md)
