# Issue #179: 多言語認証メールセットアップガイド

## 概要

ユーザーのアプリ言語設定に基づいて、認証メール（確認メール、パスワードリセット等）を適切な言語で送信する機能。

## アーキテクチャ

```
サインアップ時:
  Baketa App → ILocalizationService.CurrentCulture
    → SignupViewModel → userMetadata: { language: "ja" }
      → Supabase Auth → user_metadata に保存

メール送信時:
  Supabase Auth Event (signup, recovery, etc.)
    → Auth Hook → Edge Function (send-auth-email)
      → user_metadata.language を読み取り
        → Resend API で多言語メール送信
```

## 前提条件

- Supabase CLIがインストール済み: `npm install -g supabase`
- Resend アカウントとAPI Key
- ドメイン `mail.baketa.app` がResendで設定済み

## セットアップ手順

### 1. Supabase CLIでプロジェクトをリンク

```bash
cd E:\dev\Baketa
supabase login
supabase link --project-ref kajsoietcikivrwidqcs
```

### 2. Edge Function用の環境変数を設定

Supabase Dashboard で設定:
1. **Settings** → **Edge Functions** → **Secrets**
2. 以下のシークレットを追加:

| 名前 | 値 | 説明 |
|------|-----|------|
| `RESEND_API_KEY` | `re_xxxxxxxx` | Resend APIキー |

### 3. Edge Functionをデプロイ

```bash
supabase functions deploy send-auth-email --project-ref kajsoietcikivrwidqcs
```

### 4. Auth Hookを有効化

Supabase Dashboard で設定:
1. **Authentication** → **Hooks** (Beta)
2. **Send Email** hook を有効化
3. Function: `send-auth-email` を選択

または、SQL で直接設定:

```sql
-- Auth hookを有効化
ALTER ROLE authenticator SET pgsodium.secret_key TO 'your-secret-key';

-- Hookを設定（Dashboard推奨）
```

### 5. 標準メールテンプレートを無効化

Supabase Dashboard で設定:
1. **Authentication** → **Email Templates**
2. 各テンプレートの「Enable custom email」をONに
3. Edge Functionがメール送信を担当するため、標準テンプレートは使用されない

## サポートするメールタイプ

| タイプ | 日本語 | 英語 | トリガー |
|--------|--------|------|---------|
| `signup` | メールアドレスの確認 | Confirm your email | 新規登録 |
| `recovery` | パスワードのリセット | Reset your password | パスワードリセット要求 |
| `magic_link` | ログインリンク | Your login link | マジックリンク要求 |
| `email_change` | メールアドレス変更の確認 | Confirm email change | メールアドレス変更 |

## 言語判定ロジック

```typescript
function getUserLanguage(userMetadata?: Record<string, unknown>): "ja" | "en" {
  const lang = userMetadata?.language as string | undefined;

  // 完全一致
  if (lang === "ja" || lang === "en") return lang;

  // プレフィックス一致 (e.g., "ja-JP" → "ja")
  if (lang?.startsWith("ja")) return "ja";
  if (lang?.startsWith("en")) return "en";

  // デフォルト: 日本語
  return "ja";
}
```

## テスト方法

### ローカルテスト

```bash
# Edge Functionをローカルで起動
supabase functions serve send-auth-email --env-file ./supabase/.env.local

# テストリクエスト送信
curl -i --location --request POST 'http://localhost:54321/functions/v1/send-auth-email' \
  --header 'Content-Type: application/json' \
  --data '{
    "user": {
      "id": "test-user-id",
      "email": "test@example.com",
      "user_metadata": { "language": "en" }
    },
    "email_data": {
      "token": "test-token",
      "token_hash": "test-hash",
      "redirect_to": "https://baketa.app",
      "email_action_type": "signup",
      "site_url": "https://baketa.app"
    }
  }'
```

### 本番テスト

1. アプリで言語を英語に設定
2. 新規アカウントを作成
3. 英語の確認メールが届くことを確認

## トラブルシューティング

### メールが届かない

1. Supabase Dashboard → **Edge Functions** → **Logs** を確認
2. Resend Dashboard で送信履歴を確認
3. スパムフォルダを確認

### 日本語で届くべきなのに英語で届く

1. Supabase Dashboard → **Authentication** → **Users**
2. 該当ユーザーの `user_metadata` を確認
3. `language` フィールドが正しく設定されているか確認

### Edge Functionがタイムアウト

- Resend APIの応答が遅い場合がある
- Edge Functionのタイムアウトは60秒
- 通常は数秒で完了

## Phase 2との連携

Phase 2で実装した `SignupViewModel` が `user_metadata.language` を設定:

```csharp
// SignupViewModel.cs
var userMetadata = new Dictionary<string, object>
{
    { UserMetadataKeys.Language, _localizationService.CurrentCulture.TwoLetterISOLanguageName }
};
var result = await _authService.SignUpWithEmailPasswordAsync(Email, Password, userMetadata);
```

## ファイル構成

```
supabase/
├── config.toml                    # プロジェクト設定
└── functions/
    └── send-auth-email/
        └── index.ts               # Edge Function本体
```

## 参考リンク

- [Supabase Auth Hooks](https://supabase.com/docs/guides/auth/auth-hooks)
- [Supabase Edge Functions](https://supabase.com/docs/guides/functions)
- [Resend API](https://resend.com/docs/api-reference/emails/send-email)
