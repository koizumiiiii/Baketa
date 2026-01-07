-- ============================================================
-- Issue #261: 同意記録テーブル
-- GDPR/CCPA監査ログ用
-- ============================================================

CREATE TABLE IF NOT EXISTS consent_records (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,

    -- 同意の種類: 'privacy_policy', 'terms_of_service'
    consent_type VARCHAR(50) NOT NULL,

    -- 同意状況: 'granted' (同意), 'revoked' (撤回)
    status VARCHAR(20) NOT NULL DEFAULT 'granted',

    -- 同意したドキュメントのバージョン (例: '2026-01')
    version VARCHAR(20) NOT NULL,

    -- 記録日時 (UTC)
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    -- クライアントバージョン (監査用)
    client_version VARCHAR(20),

    -- その他の監査情報 (User-Agent, IPなど)
    metadata JSONB DEFAULT '{}'::jsonb,

    -- 制約
    CONSTRAINT valid_consent_type CHECK (consent_type IN ('privacy_policy', 'terms_of_service')),
    CONSTRAINT valid_status CHECK (status IN ('granted', 'revoked'))
);

-- 最新の同意状況を高速に取得するためのインデックス
CREATE INDEX IF NOT EXISTS idx_consent_lookup
    ON consent_records (user_id, consent_type, recorded_at DESC);

-- RLS (Row Level Security) 有効化
ALTER TABLE consent_records ENABLE ROW LEVEL SECURITY;

-- ユーザーは自分の同意記録のみ参照可能
CREATE POLICY "Users can view own consent records"
    ON consent_records FOR SELECT
    USING (auth.uid() = user_id);

-- ユーザーは自分の同意記録を作成可能
CREATE POLICY "Users can insert own consent records"
    ON consent_records FOR INSERT
    WITH CHECK (auth.uid() = user_id);

-- 同意記録の更新・削除は禁止（監査ログの完全性保持）
-- UPDATE/DELETE ポリシーは作成しない

-- ============================================================
-- 最新の同意状況を取得するビュー（便利関数）
-- ============================================================
CREATE OR REPLACE VIEW latest_consent_status AS
SELECT DISTINCT ON (user_id, consent_type)
    user_id,
    consent_type,
    status,
    version,
    recorded_at
FROM consent_records
ORDER BY user_id, consent_type, recorded_at DESC;
