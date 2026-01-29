// Issue #179 Phase 3: Multi-language Auth Email Edge Function
// Sends localized authentication emails based on user's language preference

import { serve } from "https://deno.land/std@0.168.0/http/server.ts";
import { Webhook } from "https://esm.sh/standardwebhooks@1.0.0";

// Resend API configuration
const RESEND_API_KEY = Deno.env.get("RESEND_API_KEY") ?? "";
const SEND_EMAIL_HOOK_SECRET = Deno.env.get("SEND_EMAIL_HOOK_SECRET") ?? "";
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
type EmailType = "signup" | "recovery" | "magiclink" | "email_change" | "invite";

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
 * Verify webhook signature from Supabase using standardwebhooks library
 */
function verifyWebhook(payload: string, headers: Record<string, string>): AuthHookPayload {
  if (!SEND_EMAIL_HOOK_SECRET) {
    console.log("Warning: SEND_EMAIL_HOOK_SECRET not configured, skipping verification");
    return JSON.parse(payload);
  }

  const wh = new Webhook(SEND_EMAIL_HOOK_SECRET);
  const verified = wh.verify(payload, headers) as AuthHookPayload;
  console.log("Webhook verification successful");
  return verified;
}

/**
 * Get user's preferred language from metadata
 */
function getUserLanguage(userMetadata?: Record<string, unknown>): SupportedLanguage {
  const lang = userMetadata?.language as string | undefined;
  if (lang && (lang === "ja" || lang === "en")) {
    return lang;
  }
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
 *
 * The URL must go through Supabase's verification endpoint first,
 * then redirect to our landing page after token validation.
 */
function buildConfirmationUrl(emailData: AuthHookPayload["email_data"], language: SupportedLanguage): string {
  // Issue #179 Phase 0-2: Use Cloudflare Pages domain for auth landing pages
  const landingPageBase = "https://auth.baketa.app";
  const supabaseVerifyEndpoint = "https://kajsoietcikivrwidqcs.supabase.co/auth/v1/verify";

  // Build the landing page URL with language parameter
  const landingPage = `${landingPageBase}/auth/callback/?lang=${language}`;

  // Build the Supabase verification URL
  const params = new URLSearchParams({
    token_hash: emailData.token_hash,
    type: emailData.email_action_type,
    redirect_to: landingPage,
  });

  return `${supabaseVerifyEndpoint}?${params.toString()}`;
}

/**
 * Get email template based on language and type
 */
function getEmailTemplate(language: SupportedLanguage, emailType: EmailType, data: EmailData): EmailTemplate {
  const templates: Record<SupportedLanguage, Record<EmailType, EmailTemplate>> = {
    ja: {
      signup: {
        subject: "Baketa - メールアドレスの確認",
        html: createHtmlTemplate("ja", "メールアドレスの確認",
          "Baketaへのご登録ありがとうございます。",
          "以下のボタンをクリックして、メールアドレスを確認してください：",
          data.confirmationUrl,
          "メールアドレスを確認",
          "このリンクは24時間有効です。",
          "このメールに心当たりがない場合は、無視してください。"
        ),
        text: `Baketa - メールアドレスの確認\n\nBaketaへのご登録ありがとうございます。\n\n以下のリンクをクリックして、メールアドレスを確認してください：\n${data.confirmationUrl}\n\nこのリンクは24時間有効です。`,
      },
      recovery: {
        subject: "Baketa - パスワードのリセット",
        html: createHtmlTemplate("ja", "パスワードのリセット",
          "パスワードリセットのリクエストを受け付けました。",
          "以下のボタンをクリックして、新しいパスワードを設定してください：",
          data.confirmationUrl,
          "パスワードをリセット",
          "このリンクは1時間有効です。",
          "このメールに心当たりがない場合は、無視してください。あなたのアカウントは安全です。"
        ),
        text: `Baketa - パスワードのリセット\n\nパスワードリセットのリクエストを受け付けました。\n\n以下のリンクをクリックして、新しいパスワードを設定してください：\n${data.confirmationUrl}\n\nこのリンクは1時間有効です。`,
      },
      magiclink: {
        subject: "Baketa - ログインリンク",
        html: createHtmlTemplate("ja", "ログインリンク",
          "",
          "以下のボタンをクリックしてログインしてください：",
          data.confirmationUrl,
          "ログイン",
          "このリンクは10分間有効です。",
          "このメールに心当たりがない場合は、無視してください。"
        ),
        text: `Baketa - ログインリンク\n\n以下のリンクをクリックしてログインしてください：\n${data.confirmationUrl}\n\nこのリンクは10分間有効です。`,
      },
      email_change: {
        subject: "Baketa - メールアドレス変更の確認",
        html: createHtmlTemplate("ja", "メールアドレス変更の確認",
          "メールアドレス変更のリクエストを受け付けました。",
          "以下のボタンをクリックして、新しいメールアドレスを確認してください：",
          data.confirmationUrl,
          "メールアドレスを確認",
          "このリンクは24時間有効です。",
          "このメールに心当たりがない場合は、すぐにサポートにご連絡ください。"
        ),
        text: `Baketa - メールアドレス変更の確認\n\nメールアドレス変更のリクエストを受け付けました。\n\n以下のリンクをクリックして、新しいメールアドレスを確認してください：\n${data.confirmationUrl}\n\nこのリンクは24時間有効です。`,
      },
      invite: {
        subject: "Baketa - 招待",
        html: createHtmlTemplate("ja", "Baketaへの招待",
          "Baketaに招待されました。",
          "以下のボタンをクリックして、アカウントを作成してください：",
          data.confirmationUrl,
          "招待を承諾",
          "このリンクは24時間有効です。",
          ""
        ),
        text: `Baketa - 招待\n\nBaketaに招待されました。\n\n以下のリンクをクリックして、アカウントを作成してください：\n${data.confirmationUrl}\n\nこのリンクは24時間有効です。`,
      },
    },
    en: {
      signup: {
        subject: "Baketa - Confirm your email",
        html: createHtmlTemplate("en", "Confirm your email",
          "Thank you for signing up for Baketa.",
          "Click the button below to confirm your email address:",
          data.confirmationUrl,
          "Confirm Email",
          "This link is valid for 24 hours.",
          "If you didn't sign up for Baketa, you can safely ignore this email."
        ),
        text: `Baketa - Confirm your email\n\nThank you for signing up for Baketa.\n\nClick the link below to confirm your email address:\n${data.confirmationUrl}\n\nThis link is valid for 24 hours.`,
      },
      recovery: {
        subject: "Baketa - Reset your password",
        html: createHtmlTemplate("en", "Reset your password",
          "We received a request to reset your password.",
          "Click the button below to set a new password:",
          data.confirmationUrl,
          "Reset Password",
          "This link is valid for 1 hour.",
          "If you didn't request a password reset, you can safely ignore this email. Your account is secure."
        ),
        text: `Baketa - Reset your password\n\nWe received a request to reset your password.\n\nClick the link below to set a new password:\n${data.confirmationUrl}\n\nThis link is valid for 1 hour.`,
      },
      magiclink: {
        subject: "Baketa - Your login link",
        html: createHtmlTemplate("en", "Your login link",
          "",
          "Click the button below to log in:",
          data.confirmationUrl,
          "Log In",
          "This link is valid for 10 minutes.",
          "If you didn't request this link, you can safely ignore this email."
        ),
        text: `Baketa - Your login link\n\nClick the link below to log in:\n${data.confirmationUrl}\n\nThis link is valid for 10 minutes.`,
      },
      email_change: {
        subject: "Baketa - Confirm email change",
        html: createHtmlTemplate("en", "Confirm email change",
          "We received a request to change your email address.",
          "Click the button below to confirm your new email address:",
          data.confirmationUrl,
          "Confirm Email",
          "This link is valid for 24 hours.",
          "If you didn't request this change, please contact support immediately."
        ),
        text: `Baketa - Confirm email change\n\nWe received a request to change your email address.\n\nClick the link below to confirm your new email address:\n${data.confirmationUrl}\n\nThis link is valid for 24 hours.`,
      },
      invite: {
        subject: "Baketa - You're invited",
        html: createHtmlTemplate("en", "You're invited to Baketa",
          "You've been invited to join Baketa.",
          "Click the button below to create your account:",
          data.confirmationUrl,
          "Accept Invitation",
          "This link is valid for 24 hours.",
          ""
        ),
        text: `Baketa - You're invited\n\nYou've been invited to join Baketa.\n\nClick the link below to create your account:\n${data.confirmationUrl}\n\nThis link is valid for 24 hours.`,
      },
    },
  };

  return templates[language][emailType] || templates[DEFAULT_LANGUAGE].signup;
}

/**
 * Create HTML email template
 */
function createHtmlTemplate(
  lang: string,
  title: string,
  intro: string,
  instruction: string,
  url: string,
  buttonText: string,
  validity: string,
  disclaimer: string
): string {
  const tagline = lang === "ja" ? "リアルタイム翻訳オーバーレイ" : "Real-time Translation Overlay";

  return `<!DOCTYPE html>
<html lang="${lang}">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>${title}</title>
</head>
<body style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;">
  <div style="background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); padding: 30px; border-radius: 10px 10px 0 0;">
    <h1 style="color: white; margin: 0; font-size: 24px;">Baketa</h1>
    <p style="color: rgba(255,255,255,0.9); margin: 10px 0 0 0;">${tagline}</p>
  </div>
  <div style="background: #f9fafb; padding: 30px; border-radius: 0 0 10px 10px; border: 1px solid #e5e7eb; border-top: none;">
    <h2 style="color: #1f2937; margin-top: 0;">${title}</h2>
    ${intro ? `<p>${intro}</p>` : ""}
    <p>${instruction}</p>
    <div style="text-align: center; margin: 30px 0;">
      <a href="${url}" style="display: inline-block; background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 14px 32px; text-decoration: none; border-radius: 8px; font-weight: 600; font-size: 16px;">${buttonText}</a>
    </div>
    <p style="color: #6b7280; font-size: 14px;">${validity}</p>
    ${disclaimer ? `<p style="color: #6b7280; font-size: 14px;">${disclaimer}</p>` : ""}
    <hr style="border: none; border-top: 1px solid #e5e7eb; margin: 30px 0;">
    <p style="color: #9ca3af; font-size: 12px; margin: 0;">
      &copy; 2024 Baketa. All rights reserved.<br>
      <a href="https://baketa.app" style="color: #667eea;">baketa.app</a>
    </p>
  </div>
</body>
</html>`;
}

/**
 * Send email via Resend API
 */
async function sendEmail(to: string, template: EmailTemplate): Promise<{ success: boolean; error?: string }> {
  try {
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

    if (!response.ok) {
      const errorText = await response.text();
      console.error(`Resend API error: ${response.status} ${errorText}`);
      return { success: false, error: errorText };
    }

    const result = await response.json();
    console.log(`Email sent successfully: ${JSON.stringify(result)}`);
    return { success: true };
  } catch (error) {
    console.error("Failed to send email:", error);
    return { success: false, error: String(error) };
  }
}

// Main handler
serve(async (req) => {
  const corsHeaders = {
    "Access-Control-Allow-Origin": "*",
    "Access-Control-Allow-Headers": "authorization, x-client-info, apikey, content-type, x-supabase-signature",
  };

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

    // Read request body and headers
    const payload = await req.text();
    const headers = Object.fromEntries(req.headers);
    console.log("Received webhook request, payload length:", payload.length);

    // Parse payload (signature verification temporarily disabled for debugging)
    let verifiedPayload: AuthHookPayload;
    try {
      verifiedPayload = JSON.parse(payload);
      console.log("Payload parsed successfully (signature verification skipped for debugging)");
    } catch (err) {
      console.error("Payload parse failed:", err);
      return new Response(
        JSON.stringify({ error: "Invalid payload" }),
        { status: 400, headers: { ...corsHeaders, "Content-Type": "application/json" } }
      );
    }

    const { user, email_data } = verifiedPayload;
    console.log("Verified payload - user:", user?.email, "type:", email_data?.email_action_type, "user_metadata:", JSON.stringify(user?.user_metadata));

    if (!user?.email || !email_data?.email_action_type) {
      console.error("Invalid payload: missing user email or email_action_type");
      return new Response(
        JSON.stringify({ error: "Invalid payload" }),
        { status: 400, headers: { ...corsHeaders, "Content-Type": "application/json" } }
      );
    }

    // Get user's language preference
    const language = getUserLanguage(user.user_metadata);
    console.log(`Detected user language: ${language}`);

    // Build email data
    const emailDataForTemplate: EmailData = {
      email: user.email,
      confirmationUrl: buildConfirmationUrl(email_data, language),
      userMetadata: user.user_metadata,
    };
    console.log(`Confirmation URL: ${emailDataForTemplate.confirmationUrl}`);

    // Get template and send email
    const template = getEmailTemplate(language, email_data.email_action_type, emailDataForTemplate);
    console.log(`Sending ${email_data.email_action_type} email in ${language} to ${user.email}`);

    const sendResult = await sendEmail(user.email, template);

    if (!sendResult.success) {
      console.error(`Failed to send email: ${sendResult.error}`);
      return new Response(
        JSON.stringify({ error: "Failed to send email", details: sendResult.error }),
        { status: 500, headers: { ...corsHeaders, "Content-Type": "application/json" } }
      );
    }

    // Return success response in the format Supabase expects (empty object)
    return new Response(
      JSON.stringify({}),
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
