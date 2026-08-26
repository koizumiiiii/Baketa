-- ============================================================
-- Supabase Free tier 自動pause防止用 keepalive テーブル / RPC
-- 作成日: 2026-08-26
-- ============================================================
--
-- 背景:
--   公式ドキュメント (https://supabase.com/docs/guides/platform/free-project-pausing) より:
--     "A Free plan project is considered inactive if it does not receive
--      sufficient user database activity over the past week."
--     "Typically a few user requests to the database each day over the
--      previous week is enough to keep the project from being paused."
--
--   旧keepalive（2日おきにprofilesへGET 1回 = 週3〜4回）は、この
--   「毎日数回」という目安を大きく下回っていたため活動不足と判定された。
--   → 頻度を1日3回に増やすのが主対策。
--
--   本テーブル/RPCは副次的な位置づけ:
--     - 「DBへの明確なuser query」であることに曖昧さがない
--     - last_ping_at / ping_count により、pause警告が再発した際に
--       「keepaliveが実際にDBへ到達していたか」を後から検証できる
--
-- セキュリティ方針:
--   - テーブルはRLS有効、anon/authenticated へのGRANTなし（直接アクセス不可）
--   - 書き込みは引数なし SECURITY DEFINER RPC 経由のみ（対象行はid=1固定）
--   - search_path='' + 完全修飾名（Supabase公式のSECURITY DEFINERベストプラクティス）
--   - これにより service_role キーをCIに置かず anon キーのみで運用できる
--
--   注意: anonキーは秘密情報ではない（アプリに同梱・公開リポジトリに存在）ため、
--   第三者もこのRPCを呼べる。ただし影響は単一行のUPDATEのみで情報漏えいはなく、
--   同じキーで既にprofilesへのRESTアクセスが可能な以上、露出は実質増えていない。
--
-- 実行場所: Supabase SQL Editor（プロジェクト kajsoietcikivrwidqcs）
-- ============================================================

-- ============================================================
-- Step 1: テーブル作成（単一行のみ保持）
-- ============================================================
CREATE TABLE IF NOT EXISTS public.keepalive (
  id           smallint    PRIMARY KEY,
  last_ping_at timestamptz NOT NULL DEFAULT now(),
  ping_count   bigint      NOT NULL DEFAULT 0,
  CONSTRAINT keepalive_single_row CHECK (id = 1)
);

INSERT INTO public.keepalive (id) VALUES (1)
ON CONFLICT (id) DO NOTHING;

-- ============================================================
-- Step 2: 明示的GRANT / REVOKE
--   （2026-10-30以降の破壊的変更でGRANTは必須）
--   anon / authenticated / PUBLIC には一切付与しない
-- ============================================================
REVOKE ALL ON TABLE public.keepalive FROM PUBLIC, anon, authenticated;

GRANT SELECT, INSERT, UPDATE, DELETE ON public.keepalive TO service_role;

-- ============================================================
-- Step 3: RLS有効化
--   anon / authenticated 向けポリシーは意図的に作成しない
--   （直接の読み書きを禁止し、RPCのみを入口にする）
-- ============================================================
ALTER TABLE public.keepalive ENABLE ROW LEVEL SECURITY;

-- ============================================================
-- Step 4: keepalive RPC（引数なし・SECURITY DEFINER）
--   search_path='' のため、全オブジェクトを完全修飾する必要がある
-- ============================================================
CREATE OR REPLACE FUNCTION public.keepalive_ping()
RETURNS timestamptz
LANGUAGE sql
SECURITY DEFINER
SET search_path = ''
AS $$
  UPDATE public.keepalive
     SET last_ping_at = pg_catalog.now(),
         ping_count   = ping_count + 1
   WHERE id = 1
  RETURNING last_ping_at;
$$;

-- 呼び出し元を anon と service_role のみに限定
REVOKE ALL ON FUNCTION public.keepalive_ping() FROM PUBLIC, authenticated;
GRANT EXECUTE ON FUNCTION public.keepalive_ping() TO anon, service_role;

-- ============================================================
-- 動作確認
-- ============================================================
-- SELECT public.keepalive_ping();
-- SELECT * FROM public.keepalive;
--
-- pause警告が再発した場合の診断:
--   last_ping_at が直近であれば keepalive はDBに到達している
--   → 頻度不足ではなくSupabase側の判定基準の問題を疑う
