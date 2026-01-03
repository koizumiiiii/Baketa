-- ============================================
-- [Issue #252] クラッシュレポートテーブル
-- ============================================
--
-- クライアントから送信されるクラッシュレポートを保存
-- 認証不要（クラッシュ時はログインできない可能性があるため）
--
-- Supabase Console → SQL Editor で実行

-- テーブル作成
CREATE TABLE IF NOT EXISTS crash_reports (
    id UUID PRIMARY KEY,
    crash_timestamp TIMESTAMPTZ NOT NULL,
    error_message TEXT NOT NULL,
    stack_trace TEXT,
    app_version VARCHAR(50) NOT NULL,
    os_version VARCHAR(100) NOT NULL,
    include_system_info BOOLEAN DEFAULT FALSE,
    include_logs BOOLEAN DEFAULT FALSE,
    system_info JSONB,
    logs TEXT,
    client_ip VARCHAR(45),
    created_at TIMESTAMPTZ DEFAULT NOW(),

    -- メタデータ
    processed BOOLEAN DEFAULT FALSE,
    processed_at TIMESTAMPTZ,
    notes TEXT
);

-- インデックス
CREATE INDEX IF NOT EXISTS idx_crash_reports_created_at
    ON crash_reports(created_at DESC);

CREATE INDEX IF NOT EXISTS idx_crash_reports_app_version
    ON crash_reports(app_version);

CREATE INDEX IF NOT EXISTS idx_crash_reports_processed
    ON crash_reports(processed)
    WHERE processed = FALSE;

-- コメント
COMMENT ON TABLE crash_reports IS 'クラッシュレポート保存テーブル (Issue #252)';
COMMENT ON COLUMN crash_reports.id IS 'クライアント生成UUID（重複防止）';
COMMENT ON COLUMN crash_reports.crash_timestamp IS 'クラッシュ発生日時';
COMMENT ON COLUMN crash_reports.error_message IS 'エラーメッセージ';
COMMENT ON COLUMN crash_reports.stack_trace IS 'スタックトレース';
COMMENT ON COLUMN crash_reports.app_version IS 'アプリバージョン';
COMMENT ON COLUMN crash_reports.os_version IS 'OS バージョン';
COMMENT ON COLUMN crash_reports.include_system_info IS 'システム情報を含むか';
COMMENT ON COLUMN crash_reports.include_logs IS 'ログを含むか';
COMMENT ON COLUMN crash_reports.system_info IS 'システム情報（JSON）';
COMMENT ON COLUMN crash_reports.logs IS 'アプリログ（最大100KB）';
COMMENT ON COLUMN crash_reports.client_ip IS 'クライアントIP（監査用）';
COMMENT ON COLUMN crash_reports.created_at IS 'レコード作成日時';
COMMENT ON COLUMN crash_reports.processed IS '処理済みフラグ';
COMMENT ON COLUMN crash_reports.processed_at IS '処理日時';
COMMENT ON COLUMN crash_reports.notes IS '開発者メモ';

-- RLSポリシー（service_roleのみ書き込み可能）
ALTER TABLE crash_reports ENABLE ROW LEVEL SECURITY;

-- service_role用ポリシー（フルアクセス）
CREATE POLICY "Service role full access" ON crash_reports
    FOR ALL
    USING (auth.role() = 'service_role')
    WITH CHECK (auth.role() = 'service_role');

-- 匿名ユーザーは読み取り不可（セキュリティ）
-- クラッシュレポートは管理者のみがSupabase Consoleから参照

-- ============================================
-- 統計クエリ例
-- ============================================

-- 日別クラッシュ数
-- SELECT DATE(crash_timestamp) as date, COUNT(*) as crash_count
-- FROM crash_reports
-- GROUP BY DATE(crash_timestamp)
-- ORDER BY date DESC
-- LIMIT 30;

-- バージョン別クラッシュ数
-- SELECT app_version, COUNT(*) as crash_count
-- FROM crash_reports
-- GROUP BY app_version
-- ORDER BY crash_count DESC;

-- 未処理のクラッシュレポート
-- SELECT * FROM crash_reports
-- WHERE processed = FALSE
-- ORDER BY created_at DESC;
