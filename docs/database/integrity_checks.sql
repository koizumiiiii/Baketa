-- ============================================================
-- データベース整合性チェッククエリ集
-- ============================================================
--
-- 目的: 定期的にデータの整合性を確認するためのクエリ
-- 推奨: 月1回程度、または問題発生時に実行
--
-- ============================================================

-- ------------------------------------------------------------
-- 1. auth.users と profiles の整合性
-- ------------------------------------------------------------

-- 1.1 profilesが存在しないユーザー（0であるべき）
SELECT
    'missing_profiles' as check_name,
    COUNT(*) as count,
    CASE WHEN COUNT(*) = 0 THEN 'OK' ELSE 'FAIL' END as status
FROM auth.users u
LEFT JOIN public.profiles p ON u.id = p.id
WHERE p.id IS NULL;

-- 1.2 auth.usersに存在しない孤立profiles（0であるべき）
SELECT
    'orphan_profiles' as check_name,
    COUNT(*) as count,
    CASE WHEN COUNT(*) = 0 THEN 'OK' ELSE 'FAIL' END as status
FROM public.profiles p
LEFT JOIN auth.users u ON p.id = u.id
WHERE u.id IS NULL;

-- 1.3 両テーブルの件数比較
SELECT
    'user_count_match' as check_name,
    (SELECT COUNT(*) FROM auth.users) as auth_users,
    (SELECT COUNT(*) FROM public.profiles) as profiles,
    CASE
        WHEN (SELECT COUNT(*) FROM auth.users) = (SELECT COUNT(*) FROM public.profiles)
        THEN 'OK'
        ELSE 'FAIL'
    END as status;

-- ------------------------------------------------------------
-- 2. トリガーの存在確認
-- ------------------------------------------------------------

-- 2.1 handle_new_user トリガーが有効か
SELECT
    'trigger_exists' as check_name,
    CASE WHEN COUNT(*) > 0 THEN 'OK' ELSE 'FAIL - TRIGGER MISSING!' END as status
FROM pg_trigger
WHERE tgname = 'on_auth_user_created';

-- ------------------------------------------------------------
-- 3. ライセンス関連の整合性
-- ------------------------------------------------------------

-- 3.1 user_licenses に存在するが profiles に存在しないユーザー
SELECT
    'license_without_profile' as check_name,
    COUNT(*) as count,
    CASE WHEN COUNT(*) = 0 THEN 'OK' ELSE 'WARN' END as status
FROM public.user_licenses l
LEFT JOIN public.profiles p ON l.user_id = p.id
WHERE p.id IS NULL;

-- ------------------------------------------------------------
-- 4. 修復クエリ（問題が見つかった場合）
-- ------------------------------------------------------------

-- 4.1 不足しているprofilesを補完
-- INSERT INTO public.profiles (id, email)
-- SELECT u.id, COALESCE(u.email, '')
-- FROM auth.users u
-- LEFT JOIN public.profiles p ON u.id = p.id
-- WHERE p.id IS NULL
-- ON CONFLICT (id) DO NOTHING;

-- 4.2 孤立profilesを削除（注意: データ損失の可能性）
-- DELETE FROM public.profiles p
-- WHERE NOT EXISTS (SELECT 1 FROM auth.users u WHERE u.id = p.id);
