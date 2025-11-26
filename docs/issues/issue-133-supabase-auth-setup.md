# Issue #133: Supabase Auth基盤構築

**優先度**: 🔴 Critical+ (P0+)
**ステータス**: ✅ 完了 (2025-11-26)
**Epic**: ユーザー認証システム
**ラベル**: `priority: critical+`, `epic: authentication`, `type: infrastructure`, `layer: backend`

---

## 概要

Supabase認証基盤をクラウド側で構築し、OAuth認証（Google、Discord、Twitch）とEmail/Password認証を有効化します。このIssueはバックエンド設定のみを対象とし、C#クライアント統合やUI実装は後続Issueで行います。

---

## 背景・目的

### 現状の課題
- ユーザー認証機能が存在しない
- プラン管理や課金システムの土台がない
- ユーザーごとの設定保存ができない

### 目指す状態
- Supabaseプロジェクトが構築され、認証機能が有効化されている
- Google、Discord、Twitch OAuthが設定されている
- Email/Password認証が設定されている
- 認証メールが日本語で送信される
- データベーススキーマとRLSが設定されている

---

## 完了した作業

### Phase 1: Supabaseプロジェクト構築

#### 1.1 プロジェクト作成
- [x] Supabaseアカウント作成
- [x] 新規プロジェクト作成
- [x] リージョン選択（Northeast Asia - Tokyo）

#### 1.2 Authentication基本設定
- [x] Email認証有効化
- [x] Email確認設定
- [x] パスワードポリシー設定

#### 1.3 Database Schema作成

**profilesテーブル**
```sql
CREATE TABLE IF NOT EXISTS public.profiles (
    id UUID REFERENCES auth.users(id) ON DELETE CASCADE,
    email TEXT NOT NULL,
    display_name TEXT,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT TIMEZONE('utc'::text, now()) NOT NULL,
    PRIMARY KEY (id)
);
```

**RLSポリシー**
```sql
ALTER TABLE public.profiles ENABLE ROW LEVEL SECURITY;

CREATE POLICY "Users can view own profile"
ON public.profiles FOR SELECT
TO authenticated
USING (auth.uid() = id);

CREATE POLICY "Users can update own profile"
ON public.profiles FOR UPDATE
TO authenticated
USING (auth.uid() = id);

CREATE POLICY "Users can create own profile"
ON public.profiles FOR INSERT
TO authenticated
WITH CHECK (auth.uid() = id);
```

**自動プロファイル作成トリガー**
```sql
CREATE OR REPLACE FUNCTION public.handle_new_user()
RETURNS TRIGGER
LANGUAGE plpgsql
SECURITY DEFINER
AS $$
BEGIN
    INSERT INTO public.profiles (id, email, display_name)
    VALUES (NEW.id, NEW.email, NEW.raw_user_meta_data->>'display_name');
    RETURN NEW;
END;
$$;

CREATE TRIGGER on_auth_user_created
    AFTER INSERT ON auth.users
    FOR EACH ROW
    EXECUTE FUNCTION public.handle_new_user();
```

---

### Phase 2: OAuth Provider設定

#### 2.1 Google OAuth
- [x] Google Cloud Consoleでプロジェクト作成
- [x] OAuth 2.0クライアントID作成
- [x] Supabaseにクレデンシャル設定
- [x] リダイレクトURI設定

#### 2.2 Discord OAuth
- [x] Discord Developer Portalでアプリ作成
- [x] OAuth2設定
- [x] Supabaseにクレデンシャル設定
- [x] リダイレクトURI設定

#### 2.3 Twitch OAuth
- [x] Twitch Developer Consoleでアプリ作成
- [x] OAuth設定
- [x] Supabaseにクレデンシャル設定
- [x] リダイレクトURI設定

#### 2.4 Steam OpenID（延期）
- [ ] → Issue #173 へ分離
- 理由: Supabaseがネイティブサポートしていないため、カスタム実装が必要

---

### Phase 3: 設定とテスト

#### 3.1 API Key設定
- [x] anon key取得
- [x] appsettings.Local.json.template作成
- [x] Program.csでLocal設定読み込み追加

#### 3.2 Emailテンプレート設定（日本語）
- [x] Confirm signup（メールアドレス確認）
- [x] Reset password（パスワードリセット）
- [x] Magic Link（マジックリンク）

> **Note**: 多言語対応は Issue #177 で実装予定（Goテンプレート条件分岐を使用）

#### 3.3 統合テスト
- [x] REST API接続テスト
- [x] Auth Settings確認
- [x] OAuth Provider有効確認（Google, Discord, Twitch）
- [x] Profiles Table存在確認

---

## 成果物

### 設定情報

| 項目 | 値 |
|------|-----|
| Project URL | `https://kajsoietcikivrwidqcs.supabase.co` |
| Callback URL | `https://kajsoietcikivrwidqcs.supabase.co/auth/v1/callback` |
| Site URL | `http://localhost:3000` |

### 作成/更新ファイル

**新規作成**
- `Baketa.UI/appsettings.Local.json.template` - ローカル設定テンプレート
- `scripts/test_supabase_connection.ps1` - 接続テストスクリプト

**修正**
- `Baketa.UI/Program.cs` - Local設定ファイル読み込み追加

---

## 依存関係

### Blocked by
なし（このIssueが最初の認証基盤）

### Blocks
- #167: ログイン/登録UI実装
- #168: トークン管理と永続化
- #169: 認証UI拡張
- #175: プラン別広告制御

### Related
- #173: Steam OpenID認証（分離されたIssue）
- #177: 言語切替機能（メールテンプレート多言語対応）

---

## 次のステップ

1. **Issue #167**: ログイン/登録UI実装
   - Supabase C#クライアント追加
   - `IAuthenticationService` 実装
   - LoginView作成

2. **Issue #168**: トークン管理と永続化
   - Windows Credential Manager統合
   - 自動ログイン機能

3. **Issue #173**: Steam OpenID認証
   - カスタムOpenID実装
   - Edge Function作成

---

## テスト結果

```
======================================
 Supabase Connection Test
======================================

URL: https://kajsoietcikivrwidqcs.supabase.co

[1/3] REST API Health Check...
  [OK] REST API connection successful

[2/3] Auth Settings Check...
  [OK] Auth Settings retrieved successfully

  OAuth Providers:
    - Google:  True
    - Discord: True
    - Twitch:  True

[3/3] Profiles Table Check (RLS)...
  [OK] Profiles table exists (RLS active)

======================================
 Test Complete!
======================================
```

---

**作成日**: 2025-11-26
**完了日**: 2025-11-26
**作成者**: Claude Code
**関連ドキュメント**: `docs/issues/issue-167-login-ui.md`, `docs/issues/issue-168-token-management.md`
