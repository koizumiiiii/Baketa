# Supabase データベーススキーマ

> 最終更新: 2026-01-18

## 概要

BaketaはSupabaseをバックエンドデータベースとして使用しています。

- **プロジェクトID**: `kajsoietcikivrwidqcs`
- **リージョン**: (要確認)
- **用途**: ユーザー認証、ライセンス管理、使用統計、同意記録

---

## テーブル一覧

### Public スキーマ (10テーブル)

| テーブル名 | 用途 | 主要な外部キー |
|-----------|------|---------------|
| `profiles` | ユーザープロファイル・Patreon連携 | `id` → `auth.users.id` |
| `usage_events` | 使用統計イベント | `user_id` → `profiles.id` |
| `token_usage` | Cloud AIトークン消費量 | `user_id` → `profiles.id` |
| `bonus_tokens` | ボーナストークン付与 | `user_id` → `profiles.id` |
| `consent_records` | GDPR/CCPA同意記録 | `user_id` → `profiles.id` |
| `license_history` | ライセンス変更履歴 | `user_id` → `profiles.id` |
| `promotion_codes` | プロモーションコードマスタ | - |
| `promotion_code_redemptions` | コード使用履歴 | `user_id`, `promotion_code_id` |
| `crash_reports` | クラッシュレポート | - (匿名) |
| `latest_consent_status` | 最新同意状態ビュー | - |

---

## テーブル詳細

### profiles

ユーザープロファイル。`auth.users`と1:1で対応し、Patreon連携情報を保持。

```sql
CREATE TABLE profiles (
  id UUID PRIMARY KEY,                    -- auth.users.id と同一
  email TEXT NOT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT timezone('utc', now()),
  patreon_user_id TEXT                    -- Patreon連携時に設定
);
```

| カラム | 型 | NULL | デフォルト | 説明 |
|--------|-----|------|-----------|------|
| `id` | UUID | NO | - | auth.users.id と同一 |
| `email` | TEXT | NO | - | メールアドレス |
| `created_at` | TIMESTAMPTZ | NO | now() | 作成日時 |
| `patreon_user_id` | TEXT | YES | NULL | Patreon User ID (数字文字列) |

**関連**:
- `auth.users.id` = `profiles.id` (1:1)
- Patreon連携時: `profiles.patreon_user_id` に Patreon User ID を設定

---

### usage_events

使用統計イベント。アプリの使用状況を匿名化して収集。

```sql
CREATE TABLE usage_events (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  session_id UUID NOT NULL,               -- アプリセッションID
  user_id UUID,                           -- profiles.id (オプション)
  event_type TEXT NOT NULL,               -- イベント種別
  event_data JSONB,                       -- イベント詳細データ
  schema_version INTEGER NOT NULL DEFAULT 1,
  app_version TEXT NOT NULL,
  country_code TEXT,                      -- CF-IPCountry から取得
  occurred_at TIMESTAMPTZ NOT NULL,
  created_at TIMESTAMPTZ DEFAULT now()
);
```

| カラム | 型 | NULL | 説明 |
|--------|-----|------|------|
| `id` | UUID | NO | イベントID |
| `session_id` | UUID | NO | アプリセッションID (起動ごとに生成) |
| `user_id` | UUID | YES | ユーザーID (認証済みの場合) |
| `event_type` | TEXT | NO | `session_start`, `translation` など |
| `event_data` | JSONB | YES | イベント固有データ |
| `schema_version` | INTEGER | NO | スキーマバージョン |
| `app_version` | TEXT | NO | アプリバージョン (例: "0.2.26") |
| `country_code` | TEXT | YES | 国コード (例: "JP") |
| `occurred_at` | TIMESTAMPTZ | NO | イベント発生日時 |
| `created_at` | TIMESTAMPTZ | YES | レコード作成日時 |

**event_type 一覧**:
| event_type | 説明 | event_data |
|------------|------|------------|
| `session_start` | アプリ起動 | `{app_version, os_version}` |
| `translation` | 翻訳実行 | `{source_language, target_language, engine, processing_time_ms, game_title}` |

---

### token_usage

Cloud AIトークン使用量。月単位で集計。

```sql
CREATE TABLE token_usage (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id UUID NOT NULL,                  -- profiles.id
  year_month TEXT NOT NULL,               -- "2026-01" 形式
  tokens_used BIGINT NOT NULL DEFAULT 0,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- ユニーク制約
UNIQUE (user_id, year_month)
```

| カラム | 型 | NULL | 説明 |
|--------|-----|------|------|
| `id` | UUID | NO | レコードID |
| `user_id` | UUID | NO | ユーザーID |
| `year_month` | TEXT | NO | 対象年月 (例: "2026-01") |
| `tokens_used` | BIGINT | NO | 使用トークン数 |
| `created_at` | TIMESTAMPTZ | NO | 作成日時 |
| `updated_at` | TIMESTAMPTZ | NO | 更新日時 |

---

### bonus_tokens

ボーナストークン付与。プロモーションコードや特典で付与されるトークン。

```sql
CREATE TABLE bonus_tokens (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id UUID NOT NULL,                  -- profiles.id
  source_type VARCHAR NOT NULL,           -- 付与元種別
  source_id UUID,                         -- 付与元ID (promotion_codes.id など)
  granted_tokens BIGINT NOT NULL,         -- 付与トークン数
  used_tokens BIGINT NOT NULL DEFAULT 0,  -- 使用済みトークン数
  expires_at TIMESTAMPTZ NOT NULL,        -- 有効期限
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
```

| カラム | 型 | NULL | 説明 |
|--------|-----|------|------|
| `id` | UUID | NO | レコードID |
| `user_id` | UUID | NO | ユーザーID |
| `source_type` | VARCHAR | NO | 付与元種別 (`promotion`, `referral` など) |
| `source_id` | UUID | YES | 付与元ID |
| `granted_tokens` | BIGINT | NO | 付与トークン数 |
| `used_tokens` | BIGINT | NO | 使用済みトークン数 |
| `expires_at` | TIMESTAMPTZ | NO | 有効期限 |
| `created_at` | TIMESTAMPTZ | NO | 作成日時 |
| `updated_at` | TIMESTAMPTZ | NO | 更新日時 |

---

### consent_records

GDPR/CCPA同意記録。法的監査用。

```sql
CREATE TABLE consent_records (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id UUID NOT NULL,                  -- profiles.id
  consent_type VARCHAR NOT NULL,          -- 同意種別
  status VARCHAR NOT NULL DEFAULT 'granted',
  version VARCHAR NOT NULL,               -- 同意したバージョン
  recorded_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  client_version VARCHAR,                 -- アプリバージョン
  metadata JSONB DEFAULT '{}'::jsonb
);
```

| カラム | 型 | NULL | 説明 |
|--------|-----|------|------|
| `id` | UUID | NO | レコードID |
| `user_id` | UUID | NO | ユーザーID |
| `consent_type` | VARCHAR | NO | `privacy_policy`, `terms_of_service` |
| `status` | VARCHAR | NO | `granted`, `revoked` |
| `version` | VARCHAR | NO | 同意したドキュメントバージョン |
| `recorded_at` | TIMESTAMPTZ | NO | 同意日時 |
| `client_version` | VARCHAR | YES | アプリバージョン |
| `metadata` | JSONB | YES | 追加メタデータ |

---

### license_history

ライセンス変更履歴。プラン変更の監査ログ。

```sql
CREATE TABLE license_history (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id UUID,                           -- profiles.id (Supabaseユーザー)
  patreon_user_id TEXT,                   -- Patreon User ID
  old_plan TEXT,
  new_plan TEXT NOT NULL,
  changed_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  source TEXT NOT NULL,                   -- 変更元 (webhook, manual など)
  patreon_pledge_id TEXT,
  metadata JSONB DEFAULT '{}'::jsonb
);
```

**注意**: `user_id` と `patreon_user_id` の両方がNULL許可されているが、どちらか一方は設定されるべき。

---

### promotion_codes

プロモーションコードマスタ。

```sql
CREATE TABLE promotion_codes (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  code TEXT NOT NULL UNIQUE,
  plan_type INTEGER NOT NULL,             -- 1=Pro, 2=Premium, 3=Ultimate
  expires_at TIMESTAMPTZ NOT NULL,
  duration_days INTEGER NOT NULL DEFAULT 30,
  usage_type TEXT NOT NULL DEFAULT 'single_use',
  max_uses INTEGER DEFAULT 1,
  current_uses INTEGER DEFAULT 0,
  created_at TIMESTAMPTZ DEFAULT now(),
  is_active BOOLEAN DEFAULT true,
  description TEXT
);
```

---

### promotion_code_redemptions

プロモーションコード使用履歴。

```sql
CREATE TABLE promotion_code_redemptions (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  promotion_code_id UUID REFERENCES promotion_codes(id),
  user_id UUID,                           -- profiles.id
  redeemed_at TIMESTAMPTZ DEFAULT now(),
  status TEXT NOT NULL,                   -- 'success', 'failed' など
  error_message TEXT,
  client_ip TEXT
);
```

---

### crash_reports

クラッシュレポート。匿名で収集。

```sql
CREATE TABLE crash_reports (
  id UUID PRIMARY KEY,
  crash_timestamp TIMESTAMPTZ NOT NULL,
  error_message TEXT NOT NULL,
  stack_trace TEXT,
  app_version TEXT NOT NULL,
  os_version TEXT NOT NULL,
  include_system_info BOOLEAN DEFAULT false,
  include_logs BOOLEAN DEFAULT false,
  system_info JSONB,
  logs TEXT,
  client_ip TEXT,
  created_at TIMESTAMPTZ DEFAULT now()
);
```

**注意**: `user_id` カラムなし。プライバシー保護のため匿名。

---

### latest_consent_status

最新の同意状態を取得するビュー（推定）。

```sql
-- ビュー定義（推定）
CREATE VIEW latest_consent_status AS
SELECT DISTINCT ON (user_id, consent_type)
  user_id,
  consent_type,
  status,
  version,
  recorded_at
FROM consent_records
ORDER BY user_id, consent_type, recorded_at DESC;
```

---

## Auth スキーマ

Supabase Auth が管理するテーブル。直接操作は非推奨。

### auth.users

認証済みユーザー。

| カラム | 型 | 説明 |
|--------|-----|------|
| `id` | UUID | ユーザーID (profiles.id と同一) |
| `email` | VARCHAR | メールアドレス |
| `encrypted_password` | VARCHAR | ハッシュ化パスワード |
| `email_confirmed_at` | TIMESTAMPTZ | メール確認日時 |
| `last_sign_in_at` | TIMESTAMPTZ | 最終ログイン日時 |
| `raw_app_meta_data` | JSONB | アプリメタデータ |
| `raw_user_meta_data` | JSONB | ユーザーメタデータ |
| `created_at` | TIMESTAMPTZ | 作成日時 |

### auth.sessions

アクティブセッション。

### auth.refresh_tokens

リフレッシュトークン。

---

## RPC (Stored Procedures)

### link_patreon_user

Supabaseユーザーと Patreon ユーザーを紐付け。

```sql
-- 呼び出し例
SELECT link_patreon_user(
  p_user_id := '673debd1-a20e-42bb-8e4c-1cda317e75d1',
  p_patreon_user_id := '197440691'
);
```

---

## インデックス (推定)

```sql
-- パフォーマンス用インデックス（推定）
CREATE INDEX idx_profiles_patreon_user_id ON profiles(patreon_user_id);
CREATE INDEX idx_usage_events_user_id ON usage_events(user_id);
CREATE INDEX idx_usage_events_occurred_at ON usage_events(occurred_at);
CREATE INDEX idx_token_usage_user_id_year_month ON token_usage(user_id, year_month);
CREATE INDEX idx_bonus_tokens_user_id ON bonus_tokens(user_id);
CREATE INDEX idx_consent_records_user_id ON consent_records(user_id);
```

---

## Row Level Security (RLS)

Supabase の RLS ポリシー設定（要確認）。

```sql
-- 推定: profiles は自分のレコードのみ読み書き可能
ALTER TABLE profiles ENABLE ROW LEVEL SECURITY;

CREATE POLICY "Users can view own profile"
  ON profiles FOR SELECT
  USING (auth.uid() = id);

CREATE POLICY "Users can update own profile"
  ON profiles FOR UPDATE
  USING (auth.uid() = id);
```

---

## 関連ドキュメント

- [Cloudflare KV スキーマ](./cloudflare-kv-schema.md)
- [認証フロー](../auth/authentication-flow.md)
- [外部サービス連携](../external-services.md)
