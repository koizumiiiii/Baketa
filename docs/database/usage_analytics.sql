-- ===========================================
-- Baketa 使用統計収集システム
-- Issue #269: usage_events テーブル
-- Issue #270: device_id カラム削除
-- ===========================================
--
-- 実行手順:
-- 1. Supabase Console → SQL Editor でこのSQLを実行
-- 2. Relay Serverに /api/analytics/events エンドポイントを追加
-- 3. クライアント側にUsageAnalyticsServiceを実装
--
-- ===========================================

-- ===========================================
-- Issue #270: device_id カラム削除
-- ===========================================
-- 未使用のdevice_idカラムを削除（NULLのみが入っている）
ALTER TABLE promotion_code_redemptions DROP COLUMN IF EXISTS device_id;

-- ===========================================
-- Issue #269: 使用統計イベントテーブル
-- ===========================================
-- Geminiレビュー反映: セッションテーブルを廃止し、イベントテーブルに統合
CREATE TABLE IF NOT EXISTS usage_events (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

  -- セッション識別（クライアント生成UUID、FKなし）
  session_id UUID NOT NULL,

  -- ユーザー識別（匿名利用可：NULL許容）
  user_id UUID REFERENCES auth.users(id) ON DELETE SET NULL,

  -- イベント情報
  event_type TEXT NOT NULL,  -- 'session_start', 'session_end', 'translation', 'capture_start'
  event_data JSONB,          -- 柔軟なデータ格納
  schema_version INT NOT NULL DEFAULT 1,  -- 将来の互換性用

  -- アプリケーション情報
  app_version TEXT NOT NULL,

  -- 地理情報（Cloudflare CFヘッダーから取得）
  country_code TEXT,

  -- タイムスタンプ
  occurred_at TIMESTAMPTZ NOT NULL,  -- イベント発生時刻（クライアント時刻）
  created_at TIMESTAMPTZ DEFAULT NOW()  -- DB挿入時刻
);

-- ===========================================
-- インデックス（クエリ性能対策）
-- ===========================================

-- セッション単位の分析用
CREATE INDEX IF NOT EXISTS idx_usage_events_session
  ON usage_events(session_id);

-- イベントタイプ別分析用
CREATE INDEX IF NOT EXISTS idx_usage_events_type
  ON usage_events(event_type);

-- 時系列分析用
CREATE INDEX IF NOT EXISTS idx_usage_events_occurred
  ON usage_events(occurred_at);

-- ユーザー別分析用
CREATE INDEX IF NOT EXISTS idx_usage_events_user
  ON usage_events(user_id);

-- JSONB内の頻繁に検索するキー用インデックス
CREATE INDEX IF NOT EXISTS idx_usage_events_game_title
  ON usage_events ((event_data->>'game_title'));

CREATE INDEX IF NOT EXISTS idx_usage_events_mode
  ON usage_events ((event_data->>'mode'));

CREATE INDEX IF NOT EXISTS idx_usage_events_source_lang
  ON usage_events ((event_data->>'source_lang'));

-- 90日経過データの自動削除用
CREATE INDEX IF NOT EXISTS idx_usage_events_created
  ON usage_events(created_at);

-- ===========================================
-- RLS (Row Level Security) 設定
-- ===========================================
ALTER TABLE usage_events ENABLE ROW LEVEL SECURITY;

-- Service roleのみが書き込み可能（Relay Server経由）
DROP POLICY IF EXISTS "Service role can insert usage_events" ON usage_events;
CREATE POLICY "Service role can insert usage_events" ON usage_events
  FOR INSERT TO service_role
  WITH CHECK (true);

-- Service roleのみが読み取り可能（分析用）
DROP POLICY IF EXISTS "Service role can select usage_events" ON usage_events;
CREATE POLICY "Service role can select usage_events" ON usage_events
  FOR SELECT TO service_role
  USING (true);

-- ===========================================
-- 90日経過データの自動削除関数
-- ===========================================
-- Supabase Edge Functionまたはpg_cronで定期実行
CREATE OR REPLACE FUNCTION cleanup_old_usage_events()
RETURNS INTEGER
LANGUAGE plpgsql
SECURITY DEFINER
AS $$
DECLARE
  deleted_count INTEGER;
BEGIN
  DELETE FROM usage_events
  WHERE created_at < NOW() - INTERVAL '90 days';

  GET DIAGNOSTICS deleted_count = ROW_COUNT;

  RAISE LOG 'cleanup_old_usage_events: Deleted % rows', deleted_count;

  RETURN deleted_count;
END;
$$;

-- ===========================================
-- 分析用クエリ例
-- ===========================================

-- セッション数（日別）
-- SELECT
--   DATE(occurred_at) as date,
--   COUNT(DISTINCT session_id) as sessions
-- FROM usage_events
-- WHERE event_type = 'session_start'
-- GROUP BY DATE(occurred_at)
-- ORDER BY date DESC;

-- ゲームタイトル別翻訳回数
-- SELECT
--   event_data->>'game_title' as game,
--   COUNT(*) as translations
-- FROM usage_events
-- WHERE event_type = 'translation'
-- GROUP BY event_data->>'game_title'
-- ORDER BY translations DESC
-- LIMIT 20;

-- 翻訳モード別使用率
-- SELECT
--   event_data->>'mode' as mode,
--   COUNT(*) as count,
--   ROUND(COUNT(*) * 100.0 / SUM(COUNT(*)) OVER(), 2) as percentage
-- FROM usage_events
-- WHERE event_type = 'translation'
-- GROUP BY event_data->>'mode';

-- 国別アクティブユーザー
-- SELECT
--   country_code,
--   COUNT(DISTINCT session_id) as sessions
-- FROM usage_events
-- WHERE event_type = 'session_start'
--   AND occurred_at > NOW() - INTERVAL '30 days'
-- GROUP BY country_code
-- ORDER BY sessions DESC;

-- 言語ペア別使用状況
-- SELECT
--   event_data->>'source_lang' as source,
--   event_data->>'target_lang' as target,
--   COUNT(*) as count
-- FROM usage_events
-- WHERE event_type = 'translation'
-- GROUP BY event_data->>'source_lang', event_data->>'target_lang'
-- ORDER BY count DESC;
