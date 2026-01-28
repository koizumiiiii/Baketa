// Issue #179 Phase 3: Multi-language Auth Email Edge Function
// Sends localized authentication emails based on user's language preference

import { serve } from "https://deno.land/std@0.168.0/http/server.ts";

// Resend API configuration
const RESEND_API_KEY = Deno.env.get("RESEND_API_KEY") ?? "";
const FROM_EMAIL = "Baketa <noreply@mail.baketa.app>";

// Supported languages
type SupportedLanguage = "ja" | "en";
const DEFAULT_LANGUAGE: SupportedLanguage = "ja";

// Email templates interface
interface EmailTemplate {
  subject: string;
  html: string;
  text: string;
}

// Template types
type EmailType = "signup" | "recovery" | "magic_link" | "email_change";

// Email templates for each language
const templates: Record<SupportedLanguage, Record<EmailType, (data: EmailData) => EmailTemplate>> = {
  ja: {
    signup: (data) => ({
      subject: "Baketa - メールアドレスの確認",
      html: `
<!DOCTYPE html>
<html lang="ja">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>メールアドレスの確認</title>
</head>
<body style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;">
  <div style="background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); padding: 30px; border-radius: 10px 10px 0 0;">
    <h1 style="color: white; margin: 0; font-size: 24px;">Baketa</h1>
    <p style="color: rgba(255,255,255,0.9); margin: 10px 0 0 0;">リアルタイム翻訳オーバーレイ</p>
  </div>
  <div style="background: #f9fafb; padding: 30px; border-radius: 0 0 10px 10px; border: 1px solid #e5e7eb; border-top: none;">
    <h2 style="color: #1f2937; margin-top: 0;">メールアドレスの確認</h2>
    <p>Baketaへのご登録ありがとうございます。</p>
    <p>以下のボタンをクリックして、メールアドレスを確認してください：</p>
    <div style="text-align: center; margin: 30px 0;">
      <a href="${data.confirmationUrl}" style="display: inline-block; background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 14px 32px; text-decoration: none; border-radius: 8px; font-weight: 600; font-size: 16px;">メールアドレスを確認</a>
    </div>
    <p style="color: #6b7280; font-size: 14px;">このリンクは24時間有効です。</p>
    <p style="color: #6b7280; font-size: 14px;">このメールに心当たりがない場合は、無視してください。</p>
    <hr style="border: none; border-top: 1px solid #e5e7eb; margin: 30px 0;">
    <p style="color: #9ca3af; font-size: 12px; margin: 0;">
      &copy; 2024 Baketa. All rights reserved.<br>
      <a href="https://baketa.app" style="color: #667eea;">baketa.app</a>
    </p>
  </div>
</body>
</html>`,
      text: `Baketa - メールアドレスの確認

Baketaへのご登録ありがとうございます。

以下のリンクをクリックして、メールアドレスを確認してください：
${data.confirmationUrl}

このリンクは24時間有効です。

このメールに心当たりがない場合は、無視してください。

---
Baketa - リアルタイム翻訳オーバーレイ
https://baketa.app`,
    }),

    recovery: (data) => ({
      subject: "Baketa - パスワードのリセット",
      html: `
<!DOCTYPE html>
<html lang="ja">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>パスワードのリセット</title>
</head>
<body style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;">
  <div style="background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); padding: 30px; border-radius: 10px 10px 0 0;">
    <h1 style="color: white; margin: 0; font-size: 24px;">Baketa</h1>
    <p style="color: rgba(255,255,255,0.9); margin: 10px 0 0 0;">リアルタイム翻訳オーバーレイ</p>
  </div>
  <div style="background: #f9fafb; padding: 30px; border-radius: 0 0 10px 10px; border: 1px solid #e5e7eb; border-top: none;">
    <h2 style="color: #1f2937; margin-top: 0;">パスワードのリセット</h2>
    <p>パスワードリセットのリクエストを受け付けました。</p>
    <p>以下のボタンをクリックして、新しいパスワードを設定してください：</p>
    <div style="text-align: center; margin: 30px 0;">
      <a href="${data.confirmationUrl}" style="display: inline-block; background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 14px 32px; text-decoration: none; border-radius: 8px; font-weight: 600; font-size: 16px;">パスワードをリセット</a>
    </div>
    <p style="color: #6b7280; font-size: 14px;">このリンクは1時間有効です。</p>
    <p style="color: #6b7280; font-size: 14px;">このメールに心当たりがない場合は、無視してください。あなたのアカウントは安全です。</p>
    <hr style="border: none; border-top: 1px solid #e5e7eb; margin: 30px 0;">
    <p style="color: #9ca3af; font-size: 12px; margin: 0;">
      &copy; 2024 Baketa. All rights reserved.<br>
      <a href="https://baketa.app" style="color: #667eea;">baketa.app</a>
    </p>
  </div>
</body>
</html>`,
      text: `Baketa - パスワードのリセット

パスワードリセットのリクエストを受け付けました。

以下のリンクをクリックして、新しいパスワードを設定してください：
${data.confirmationUrl}

このリンクは1時間有効です。

このメールに心当たりがない場合は、無視してください。あなたのアカウントは安全です。

---
Baketa - リアルタイム翻訳オーバーレイ
https://baketa.app`,
    }),

    magic_link: (data) => ({
      subject: "Baketa - ログインリンク",
      html: `
<!DOCTYPE html>
<html lang="ja">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>ログインリンク</title>
</head>
<body style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;">
  <div style="background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); padding: 30px; border-radius: 10px 10px 0 0;">
    <h1 style="color: white; margin: 0; font-size: 24px;">Baketa</h1>
    <p style="color: rgba(255,255,255,0.9); margin: 10px 0 0 0;">リアルタイム翻訳オーバーレイ</p>
  </div>
  <div style="background: #f9fafb; padding: 30px; border-radius: 0 0 10px 10px; border: 1px solid #e5e7eb; border-top: none;">
    <h2 style="color: #1f2937; margin-top: 0;">ログインリンク</h2>
    <p>以下のボタンをクリックしてログインしてください：</p>
    <div style="text-align: center; margin: 30px 0;">
      <a href="${data.confirmationUrl}" style="display: inline-block; background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 14px 32px; text-decoration: none; border-radius: 8px; font-weight: 600; font-size: 16px;">ログイン</a>
    </div>
    <p style="color: #6b7280; font-size: 14px;">このリンクは10分間有効です。</p>
    <p style="color: #6b7280; font-size: 14px;">このメールに心当たりがない場合は、無視してください。</p>
    <hr style="border: none; border-top: 1px solid #e5e7eb; margin: 30px 0;">
    <p style="color: #9ca3af; font-size: 12px; margin: 0;">
      &copy; 2024 Baketa. All rights reserved.<br>
      <a href="https://baketa.app" style="color: #667eea;">baketa.app</a>
    </p>
  </div>
</body>
</html>`,
      text: `Baketa - ログインリンク

以下のリンクをクリックしてログインしてください：
${data.confirmationUrl}

このリンクは10分間有効です。

このメールに心当たりがない場合は、無視してください。

---
Baketa - リアルタイム翻訳オーバーレイ
https://baketa.app`,
    }),

    email_change: (data) => ({
      subject: "Baketa - メールアドレス変更の確認",
      html: `
<!DOCTYPE html>
<html lang="ja">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>メールアドレス変更の確認</title>
</head>
<body style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;">
  <div style="background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); padding: 30px; border-radius: 10px 10px 0 0;">
    <h1 style="color: white; margin: 0; font-size: 24px;">Baketa</h1>
    <p style="color: rgba(255,255,255,0.9); margin: 10px 0 0 0;">リアルタイム翻訳オーバーレイ</p>
  </div>
  <div style="background: #f9fafb; padding: 30px; border-radius: 0 0 10px 10px; border: 1px solid #e5e7eb; border-top: none;">
    <h2 style="color: #1f2937; margin-top: 0;">メールアドレス変更の確認</h2>
    <p>メールアドレス変更のリクエストを受け付けました。</p>
    <p>以下のボタンをクリックして、新しいメールアドレスを確認してください：</p>
    <div style="text-align: center; margin: 30px 0;">
      <a href="${data.confirmationUrl}" style="display: inline-block; background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 14px 32px; text-decoration: none; border-radius: 8px; font-weight: 600; font-size: 16px;">メールアドレスを確認</a>
    </div>
    <p style="color: #6b7280; font-size: 14px;">このリンクは24時間有効です。</p>
    <p style="color: #6b7280; font-size: 14px;">このメールに心当たりがない場合は、すぐにサポートにご連絡ください。</p>
    <hr style="border: none; border-top: 1px solid #e5e7eb; margin: 30px 0;">
    <p style="color: #9ca3af; font-size: 12px; margin: 0;">
      &copy; 2024 Baketa. All rights reserved.<br>
      <a href="https://baketa.app" style="color: #667eea;">baketa.app</a>
    </p>
  </div>
</body>
</html>`,
      text: `Baketa - メールアドレス変更の確認

メールアドレス変更のリクエストを受け付けました。

以下のリンクをクリックして、新しいメールアドレスを確認してください：
${data.confirmationUrl}

このリンクは24時間有効です。

このメールに心当たりがない場合は、すぐにサポートにご連絡ください。

---
Baketa - リアルタイム翻訳オーバーレイ
https://baketa.app`,
    }),
  },

  en: {
    signup: (data) => ({
      subject: "Baketa - Confirm your email",
      html: `
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>Confirm your email</title>
</head>
<body style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;">
  <div style="background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); padding: 30px; border-radius: 10px 10px 0 0;">
    <h1 style="color: white; margin: 0; font-size: 24px;">Baketa</h1>
    <p style="color: rgba(255,255,255,0.9); margin: 10px 0 0 0;">Real-time Translation Overlay</p>
  </div>
  <div style="background: #f9fafb; padding: 30px; border-radius: 0 0 10px 10px; border: 1px solid #e5e7eb; border-top: none;">
    <h2 style="color: #1f2937; margin-top: 0;">Confirm your email</h2>
    <p>Thank you for signing up for Baketa.</p>
    <p>Click the button below to confirm your email address:</p>
    <div style="text-align: center; margin: 30px 0;">
      <a href="${data.confirmationUrl}" style="display: inline-block; background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 14px 32px; text-decoration: none; border-radius: 8px; font-weight: 600; font-size: 16px;">Confirm Email</a>
    </div>
    <p style="color: #6b7280; font-size: 14px;">This link is valid for 24 hours.</p>
    <p style="color: #6b7280; font-size: 14px;">If you didn't sign up for Baketa, you can safely ignore this email.</p>
    <hr style="border: none; border-top: 1px solid #e5e7eb; margin: 30px 0;">
    <p style="color: #9ca3af; font-size: 12px; margin: 0;">
      &copy; 2024 Baketa. All rights reserved.<br>
      <a href="https://baketa.app" style="color: #667eea;">baketa.app</a>
    </p>
  </div>
</body>
</html>`,
      text: `Baketa - Confirm your email

Thank you for signing up for Baketa.

Click the link below to confirm your email address:
${data.confirmationUrl}

This link is valid for 24 hours.

If you didn't sign up for Baketa, you can safely ignore this email.

---
Baketa - Real-time Translation Overlay
https://baketa.app`,
    }),

    recovery: (data) => ({
      subject: "Baketa - Reset your password",
      html: `
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>Reset your password</title>
</head>
<body style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;">
  <div style="background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); padding: 30px; border-radius: 10px 10px 0 0;">
    <h1 style="color: white; margin: 0; font-size: 24px;">Baketa</h1>
    <p style="color: rgba(255,255,255,0.9); margin: 10px 0 0 0;">Real-time Translation Overlay</p>
  </div>
  <div style="background: #f9fafb; padding: 30px; border-radius: 0 0 10px 10px; border: 1px solid #e5e7eb; border-top: none;">
    <h2 style="color: #1f2937; margin-top: 0;">Reset your password</h2>
    <p>We received a request to reset your password.</p>
    <p>Click the button below to set a new password:</p>
    <div style="text-align: center; margin: 30px 0;">
      <a href="${data.confirmationUrl}" style="display: inline-block; background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 14px 32px; text-decoration: none; border-radius: 8px; font-weight: 600; font-size: 16px;">Reset Password</a>
    </div>
    <p style="color: #6b7280; font-size: 14px;">This link is valid for 1 hour.</p>
    <p style="color: #6b7280; font-size: 14px;">If you didn't request a password reset, you can safely ignore this email. Your account is secure.</p>
    <hr style="border: none; border-top: 1px solid #e5e7eb; margin: 30px 0;">
    <p style="color: #9ca3af; font-size: 12px; margin: 0;">
      &copy; 2024 Baketa. All rights reserved.<br>
      <a href="https://baketa.app" style="color: #667eea;">baketa.app</a>
    </p>
  </div>
</body>
</html>`,
      text: `Baketa - Reset your password

We received a request to reset your password.

Click the link below to set a new password:
${data.confirmationUrl}

This link is valid for 1 hour.

If you didn't request a password reset, you can safely ignore this email. Your account is secure.

---
Baketa - Real-time Translation Overlay
https://baketa.app`,
    }),

    magic_link: (data) => ({
      subject: "Baketa - Your login link",
      html: `
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>Your login link</title>
</head>
<body style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;">
  <div style="background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); padding: 30px; border-radius: 10px 10px 0 0;">
    <h1 style="color: white; margin: 0; font-size: 24px;">Baketa</h1>
    <p style="color: rgba(255,255,255,0.9); margin: 10px 0 0 0;">Real-time Translation Overlay</p>
  </div>
  <div style="background: #f9fafb; padding: 30px; border-radius: 0 0 10px 10px; border: 1px solid #e5e7eb; border-top: none;">
    <h2 style="color: #1f2937; margin-top: 0;">Your login link</h2>
    <p>Click the button below to log in:</p>
    <div style="text-align: center; margin: 30px 0;">
      <a href="${data.confirmationUrl}" style="display: inline-block; background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 14px 32px; text-decoration: none; border-radius: 8px; font-weight: 600; font-size: 16px;">Log In</a>
    </div>
    <p style="color: #6b7280; font-size: 14px;">This link is valid for 10 minutes.</p>
    <p style="color: #6b7280; font-size: 14px;">If you didn't request this link, you can safely ignore this email.</p>
    <hr style="border: none; border-top: 1px solid #e5e7eb; margin: 30px 0;">
    <p style="color: #9ca3af; font-size: 12px; margin: 0;">
      &copy; 2024 Baketa. All rights reserved.<br>
      <a href="https://baketa.app" style="color: #667eea;">baketa.app</a>
    </p>
  </div>
</body>
</html>`,
      text: `Baketa - Your login link

Click the link below to log in:
${data.confirmationUrl}

This link is valid for 10 minutes.

If you didn't request this link, you can safely ignore this email.

---
Baketa - Real-time Translation Overlay
https://baketa.app`,
    }),

    email_change: (data) => ({
      subject: "Baketa - Confirm email change",
      html: `
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>Confirm email change</title>
</head>
<body style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;">
  <div style="background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); padding: 30px; border-radius: 10px 10px 0 0;">
    <h1 style="color: white; margin: 0; font-size: 24px;">Baketa</h1>
    <p style="color: rgba(255,255,255,0.9); margin: 10px 0 0 0;">Real-time Translation Overlay</p>
  </div>
  <div style="background: #f9fafb; padding: 30px; border-radius: 0 0 10px 10px; border: 1px solid #e5e7eb; border-top: none;">
    <h2 style="color: #1f2937; margin-top: 0;">Confirm email change</h2>
    <p>We received a request to change your email address.</p>
    <p>Click the button below to confirm your new email address:</p>
    <div style="text-align: center; margin: 30px 0;">
      <a href="${data.confirmationUrl}" style="display: inline-block; background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 14px 32px; text-decoration: none; border-radius: 8px; font-weight: 600; font-size: 16px;">Confirm Email</a>
    </div>
    <p style="color: #6b7280; font-size: 14px;">This link is valid for 24 hours.</p>
    <p style="color: #6b7280; font-size: 14px;">If you didn't request this change, please contact support immediately.</p>
    <hr style="border: none; border-top: 1px solid #e5e7eb; margin: 30px 0;">
    <p style="color: #9ca3af; font-size: 12px; margin: 0;">
      &copy; 2024 Baketa. All rights reserved.<br>
      <a href="https://baketa.app" style="color: #667eea;">baketa.app</a>
    </p>
  </div>
</body>
</html>`,
      text: `Baketa - Confirm email change

We received a request to change your email address.

Click the link below to confirm your new email address:
${data.confirmationUrl}

This link is valid for 24 hours.

If you didn't request this change, please contact support immediately.

---
Baketa - Real-time Translation Overlay
https://baketa.app`,
    }),
  },
};

// Email data interface
interface EmailData {
  email: string;
  confirmationUrl: string;
  userMetadata?: Record<string, unknown>;
}

// Auth hook payload from Supabase
interface AuthHookPayload {
  user: {
    id: string;
    email: string;
    user_metadata?: Record<string, unknown>;
  };
  email_data: {
    token: string;
    token_hash: string;
    redirect_to: string;
    email_action_type: EmailType;
    site_url: string;
    token_new?: string;
    token_hash_new?: string;
  };
}

/**
 * Get user's preferred language from metadata
 */
function getUserLanguage(userMetadata?: Record<string, unknown>): SupportedLanguage {
  const lang = userMetadata?.language as string | undefined;
  if (lang && (lang === "ja" || lang === "en")) {
    return lang;
  }
  // Check for longer language codes (e.g., "ja-JP" -> "ja")
  if (lang && lang.startsWith("ja")) {
    return "ja";
  }
  if (lang && lang.startsWith("en")) {
    return "en";
  }
  return DEFAULT_LANGUAGE;
}

/**
 * Build confirmation URL from email data
 */
function buildConfirmationUrl(emailData: AuthHookPayload["email_data"]): string {
  const baseUrl = emailData.site_url || "https://baketa.app";
  const params = new URLSearchParams({
    token_hash: emailData.token_hash,
    type: emailData.email_action_type,
    redirect_to: emailData.redirect_to || baseUrl,
  });
  return `${baseUrl}/auth/confirm?${params.toString()}`;
}

/**
 * Send email via Resend API
 */
async function sendEmail(to: string, template: EmailTemplate): Promise<Response> {
  const response = await fetch("https://api.resend.com/emails", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      Authorization: `Bearer ${RESEND_API_KEY}`,
    },
    body: JSON.stringify({
      from: FROM_EMAIL,
      to: [to],
      subject: template.subject,
      html: template.html,
      text: template.text,
    }),
  });

  return response;
}

// Main handler
serve(async (req) => {
  // CORS headers
  const corsHeaders = {
    "Access-Control-Allow-Origin": "*",
    "Access-Control-Allow-Headers": "authorization, x-client-info, apikey, content-type",
  };

  // Handle CORS preflight
  if (req.method === "OPTIONS") {
    return new Response("ok", { headers: corsHeaders });
  }

  try {
    // Validate API key
    if (!RESEND_API_KEY) {
      console.error("RESEND_API_KEY is not configured");
      return new Response(
        JSON.stringify({ error: "Email service not configured" }),
        { status: 500, headers: { ...corsHeaders, "Content-Type": "application/json" } }
      );
    }

    // Parse request body
    const payload: AuthHookPayload = await req.json();
    console.log("Received auth hook payload:", JSON.stringify(payload, null, 2));

    const { user, email_data } = payload;

    if (!user?.email || !email_data?.email_action_type) {
      return new Response(
        JSON.stringify({ error: "Invalid payload" }),
        { status: 400, headers: { ...corsHeaders, "Content-Type": "application/json" } }
      );
    }

    // Get user's language preference
    const language = getUserLanguage(user.user_metadata);
    console.log(`User language preference: ${language}`);

    // Get email type
    const emailType = email_data.email_action_type;
    if (!templates[language][emailType]) {
      console.error(`Unknown email type: ${emailType}`);
      return new Response(
        JSON.stringify({ error: `Unknown email type: ${emailType}` }),
        { status: 400, headers: { ...corsHeaders, "Content-Type": "application/json" } }
      );
    }

    // Build email data
    const emailDataForTemplate: EmailData = {
      email: user.email,
      confirmationUrl: buildConfirmationUrl(email_data),
      userMetadata: user.user_metadata,
    };

    // Get template and send email
    const template = templates[language][emailType](emailDataForTemplate);
    console.log(`Sending ${emailType} email in ${language} to ${user.email}`);

    const response = await sendEmail(user.email, template);

    if (!response.ok) {
      const errorText = await response.text();
      console.error(`Resend API error: ${errorText}`);
      return new Response(
        JSON.stringify({ error: "Failed to send email", details: errorText }),
        { status: 500, headers: { ...corsHeaders, "Content-Type": "application/json" } }
      );
    }

    const result = await response.json();
    console.log(`Email sent successfully: ${JSON.stringify(result)}`);

    return new Response(
      JSON.stringify({ success: true, messageId: result.id }),
      { status: 200, headers: { ...corsHeaders, "Content-Type": "application/json" } }
    );
  } catch (error) {
    console.error("Error processing auth hook:", error);
    return new Response(
      JSON.stringify({ error: "Internal server error", details: String(error) }),
      { status: 500, headers: { ...corsHeaders, "Content-Type": "application/json" } }
    );
  }
});
