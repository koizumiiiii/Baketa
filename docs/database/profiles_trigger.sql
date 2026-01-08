-- ============================================================
-- profiles テーブル自動作成トリガー
-- ============================================================
--
-- 目的: auth.users にユーザーが作成されたとき、
--       自動的に public.profiles にレコードを作成する
--
-- 履歴:
--   2025-11-26: Issue #133 で初回作成（display_name あり）
--   2026-01-08: display_name カラム削除に伴い更新
--
-- 使用方法:
--   Supabase SQL Editor で順番に実行
-- ============================================================

-- ------------------------------------------------------------
-- 1. トリガー関数の作成/更新
-- ------------------------------------------------------------
CREATE OR REPLACE FUNCTION public.handle_new_user()
RETURNS TRIGGER
LANGUAGE plpgsql
SECURITY DEFINER
AS $$
BEGIN
    INSERT INTO public.profiles (id, email)
    VALUES (NEW.id, COALESCE(NEW.email, ''));
    RETURN NEW;
EXCEPTION WHEN OTHERS THEN
    -- エラーが発生してもユーザー作成自体は続行
    -- エラーログはSupabase Logsで確認可能
    RAISE WARNING 'handle_new_user failed for user %: %', NEW.id, SQLERRM;
    RETURN NEW;
END;
$$;

-- ------------------------------------------------------------
-- 2. トリガーの作成（存在しない場合のみ）
-- ------------------------------------------------------------
-- 注意: トリガーが既に存在する場合はエラーになります
--       その場合は DROP TRIGGER を先に実行してください
--
-- DROP TRIGGER IF EXISTS on_auth_user_created ON auth.users;

CREATE TRIGGER on_auth_user_created
    AFTER INSERT ON auth.users
    FOR EACH ROW
    EXECUTE FUNCTION public.handle_new_user();

-- ------------------------------------------------------------
-- 3. 整合性チェック（既存データの補完）
-- ------------------------------------------------------------
-- auth.users に存在するが profiles に存在しないユーザーを補完
INSERT INTO public.profiles (id, email)
SELECT u.id, COALESCE(u.email, '')
FROM auth.users u
LEFT JOIN public.profiles p ON u.id = p.id
WHERE p.id IS NULL
ON CONFLICT (id) DO NOTHING;

-- ------------------------------------------------------------
-- 4. 検証クエリ
-- ------------------------------------------------------------
-- 以下のクエリで整合性を確認できます

-- 4.1 profilesが存在しないユーザー数（0であるべき）
-- SELECT COUNT(*) as missing_profiles
-- FROM auth.users u
-- LEFT JOIN public.profiles p ON u.id = p.id
-- WHERE p.id IS NULL;

-- 4.2 両テーブルの件数比較
-- SELECT
--     (SELECT COUNT(*) FROM auth.users) as auth_users_count,
--     (SELECT COUNT(*) FROM public.profiles) as profiles_count;

-- 4.3 トリガーの存在確認
-- SELECT tgname, tgenabled FROM pg_trigger WHERE tgname = 'on_auth_user_created';

-- 4.4 トリガー関数の内容確認
-- SELECT prosrc FROM pg_proc WHERE proname = 'handle_new_user';
