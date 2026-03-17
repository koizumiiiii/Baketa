/**
 * [Issue #542] テキスト翻訳エンドポイント
 * DeepL Free → Google Translation Free → NLLBフォールバック指示
 *
 * ローカル翻訳（NLLB）の前段で高品質テキスト翻訳を提供。
 * プランに関係なく全ユーザーが利用可能。
 * APIキーはRelay Serverで管理（ユーザーに取得を求めない）。
 */

// ============================================
// 型定義
// ============================================

export interface TextTranslateEnv {
  SESSIONS: KVNamespace;
  DEEPL_API_KEY?: string;
  GOOGLE_TRANSLATE_API_KEY?: string;
  ALLOWED_ORIGINS: string;
}

interface TextTranslateRequest {
  text: string;
  source_language: string;
  target_language: string;
  request_id?: string;
}

interface TextTranslateResponse {
  success: boolean;
  translated_text?: string;
  engine?: 'deepl' | 'google';
  fallback?: 'local';
  error?: string;
  request_id: string;
}

// ============================================
// 定数
// ============================================

// 月間使用量の安全上限（80%ルール）
const DEEPL_MONTHLY_CHAR_LIMIT = 400_000;    // 50万文字の80%
const GOOGLE_MONTHLY_CHAR_LIMIT = 400_000;   // 50万文字の80%

// KVキーのプレフィックス
const KV_PREFIX_DEEPL = 'translate-text:deepl';
const KV_PREFIX_GOOGLE = 'translate-text:google';

// KV TTL（月末 + 余裕で45日）
const KV_TTL_SECONDS = 45 * 24 * 60 * 60;

// レートリミット（IPベース）
const RATE_LIMIT_MAX_REQUESTS = 30;  // 1分間に30リクエストまで
const RATE_LIMIT_WINDOW_SECONDS = 60;

// DeepL API
const DEEPL_API_URL = 'https://api-free.deepl.com/v2/translate';

// Google Translation API
const GOOGLE_TRANSLATE_API_URL = 'https://translation.googleapis.com/language/translate/v2';

// DeepL言語コードマッピング（Baketa → DeepL）
const DEEPL_LANG_MAP: Record<string, string> = {
  'en': 'EN',
  'ja': 'JA',
  'zh-CN': 'ZH-HANS',
  'zh-TW': 'ZH-HANT',
  'ko': 'KO',
  'de': 'DE',
  'fr': 'FR',
  'es': 'ES',
  'it': 'IT',
  'pt': 'PT-PT',
  'nl': 'NL',
  'pl': 'PL',
  'tr': 'TR',
  'ru': 'RU',
  'ar': 'AR',
};

// Google言語コードマッピング（Baketa → Google）
const GOOGLE_LANG_MAP: Record<string, string> = {
  'en': 'en',
  'ja': 'ja',
  'zh-CN': 'zh-CN',
  'zh-TW': 'zh-TW',
  'ko': 'ko',
  'de': 'de',
  'fr': 'fr',
  'es': 'es',
  'it': 'it',
  'pt': 'pt',
  'nl': 'nl',
  'pl': 'pl',
  'tr': 'tr',
  'ru': 'ru',
  'ar': 'ar',
};

// ============================================
// メインハンドラ
// ============================================

export async function handleTranslateText(
  request: Request,
  env: TextTranslateEnv,
  origin: string,
  allowedOrigins: string
): Promise<Response> {
  const requestId = crypto.randomUUID();

  if (request.method !== 'POST') {
    return jsonResponse({ success: false, error: 'Method not allowed', request_id: requestId }, 405, origin, allowedOrigins);
  }

  // [Issue #542] IPベースのレートリミット（乱用防止）
  const clientIp = request.headers.get('CF-Connecting-IP') || 'unknown';
  const rateLimitResult = await checkIpRateLimit(env.SESSIONS, clientIp);
  if (rateLimitResult.limited) {
    console.log(`[Issue #542] Rate limited: IP=${clientIp}, count=${rateLimitResult.count}`);
    return jsonResponse({ success: false, error: 'Too many requests', request_id: requestId }, 429, origin, allowedOrigins);
  }

  // リクエストボディ解析
  let body: TextTranslateRequest;
  try {
    body = await request.json() as TextTranslateRequest;
  } catch {
    return jsonResponse({ success: false, error: 'Invalid JSON', request_id: requestId }, 400, origin, allowedOrigins);
  }

  if (!body.text || !body.target_language) {
    return jsonResponse({ success: false, error: 'Missing text or target_language', request_id: requestId }, 400, origin, allowedOrigins);
  }

  const rid = body.request_id || requestId;
  const textLength = body.text.length;
  const yearMonth = getCurrentYearMonth();

  // 1. DeepL Free を試行
  if (env.DEEPL_API_KEY) {
    const deeplUsage = await getMonthlyUsage(env.SESSIONS, KV_PREFIX_DEEPL, yearMonth);

    if (deeplUsage + textLength <= DEEPL_MONTHLY_CHAR_LIMIT) {
      try {
        const result = await translateWithDeepL(
          body.text, body.source_language, body.target_language, env.DEEPL_API_KEY
        );

        if (result) {
          await recordUsage(env.SESSIONS, KV_PREFIX_DEEPL, yearMonth, deeplUsage + textLength);

          console.log(JSON.stringify({
            event: 'translate_text_success',
            engine: 'deepl',
            chars: textLength,
            monthly_used: deeplUsage + textLength,
            monthly_limit: DEEPL_MONTHLY_CHAR_LIMIT,
          }));

          return jsonResponse({
            success: true,
            translated_text: result,
            engine: 'deepl',
            request_id: rid,
          }, 200, origin, allowedOrigins);
        }
      } catch (err) {
        console.error(`[Issue #542] DeepL error: ${err}`);
      }
    } else {
      console.log(`[Issue #542] DeepL quota near limit: ${deeplUsage}/${DEEPL_MONTHLY_CHAR_LIMIT}`);
    }
  }

  // 2. Google Translation Free を試行
  if (env.GOOGLE_TRANSLATE_API_KEY) {
    const googleUsage = await getMonthlyUsage(env.SESSIONS, KV_PREFIX_GOOGLE, yearMonth);

    if (googleUsage + textLength <= GOOGLE_MONTHLY_CHAR_LIMIT) {
      try {
        const result = await translateWithGoogle(
          body.text, body.source_language, body.target_language, env.GOOGLE_TRANSLATE_API_KEY
        );

        if (result) {
          await recordUsage(env.SESSIONS, KV_PREFIX_GOOGLE, yearMonth, googleUsage + textLength);

          console.log(JSON.stringify({
            event: 'translate_text_success',
            engine: 'google',
            chars: textLength,
            monthly_used: googleUsage + textLength,
            monthly_limit: GOOGLE_MONTHLY_CHAR_LIMIT,
          }));

          return jsonResponse({
            success: true,
            translated_text: result,
            engine: 'google',
            request_id: rid,
          }, 200, origin, allowedOrigins);
        }
      } catch (err) {
        console.error(`[Issue #542] Google error: ${err}`);
      }
    } else {
      console.log(`[Issue #542] Google quota near limit: ${googleUsage}/${GOOGLE_MONTHLY_CHAR_LIMIT}`);
    }
  }

  // 3. フォールバック: NLLBにフォールバック指示
  console.log(JSON.stringify({
    event: 'translate_text_fallback',
    reason: 'all_engines_unavailable',
    chars: textLength,
  }));

  return jsonResponse({
    success: false,
    fallback: 'local',
    request_id: rid,
  }, 200, origin, allowedOrigins);
}

// ============================================
// DeepL API
// ============================================

async function translateWithDeepL(
  text: string,
  sourceLang: string,
  targetLang: string,
  apiKey: string
): Promise<string | null> {
  const targetDeepL = DEEPL_LANG_MAP[targetLang];
  if (!targetDeepL) return null;

  const params = new URLSearchParams();
  params.append('text', text);
  params.append('target_lang', targetDeepL);

  const sourceDeepL = DEEPL_LANG_MAP[sourceLang];
  if (sourceDeepL) {
    params.append('source_lang', sourceDeepL);
  }

  const response = await fetch(DEEPL_API_URL, {
    method: 'POST',
    headers: {
      'Authorization': `DeepL-Auth-Key ${apiKey}`,
      'Content-Type': 'application/x-www-form-urlencoded',
    },
    body: params.toString(),
  });

  if (!response.ok) {
    const errorText = await response.text();
    console.error(`[Issue #542] DeepL API error: ${response.status} ${errorText}`);
    return null;
  }

  const data = await response.json() as {
    translations: Array<{ text: string; detected_source_language: string }>;
  };

  return data.translations?.[0]?.text || null;
}

// ============================================
// Google Translation API
// ============================================

async function translateWithGoogle(
  text: string,
  sourceLang: string,
  targetLang: string,
  apiKey: string
): Promise<string | null> {
  const targetGoogle = GOOGLE_LANG_MAP[targetLang];
  if (!targetGoogle) return null;

  const params: Record<string, string> = {
    q: text,
    target: targetGoogle,
    key: apiKey,
    format: 'text',
  };

  const sourceGoogle = GOOGLE_LANG_MAP[sourceLang];
  if (sourceGoogle) {
    params.source = sourceGoogle;
  }

  const url = `${GOOGLE_TRANSLATE_API_URL}?${new URLSearchParams(params).toString()}`;

  const response = await fetch(url, { method: 'POST' });

  if (!response.ok) {
    const errorText = await response.text();
    console.error(`[Issue #542] Google Translate API error: ${response.status} ${errorText}`);
    return null;
  }

  const data = await response.json() as {
    data: {
      translations: Array<{ translatedText: string; detectedSourceLanguage?: string }>;
    };
  };

  return data.data?.translations?.[0]?.translatedText || null;
}

// ============================================
// 使用量トラッキング（Cloudflare KV）
// ============================================

async function getMonthlyUsage(kv: KVNamespace, prefix: string, yearMonth: string): Promise<number> {
  const key = `${prefix}:${yearMonth}`;
  const value = await kv.get(key);
  return value ? parseInt(value, 10) : 0;
}

async function recordUsage(kv: KVNamespace, prefix: string, yearMonth: string, totalChars: number): Promise<void> {
  const key = `${prefix}:${yearMonth}`;
  await kv.put(key, totalChars.toString(), { expirationTtl: KV_TTL_SECONDS });
}

function getCurrentYearMonth(): string {
  const now = new Date();
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}`;
}

// ============================================
// ユーティリティ
// ============================================

async function checkIpRateLimit(
  kv: KVNamespace,
  ip: string
): Promise<{ limited: boolean; count: number }> {
  const key = `ratelimit:translate-text:${ip}`;
  const value = await kv.get(key);
  const count = value ? parseInt(value, 10) : 0;

  if (count >= RATE_LIMIT_MAX_REQUESTS) {
    return { limited: true, count };
  }

  await kv.put(key, (count + 1).toString(), { expirationTtl: RATE_LIMIT_WINDOW_SECONDS });
  return { limited: false, count: count + 1 };
}

function jsonResponse(
  body: TextTranslateResponse,
  status: number,
  origin: string,
  allowedOrigins: string
): Response {
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
  };

  // CORS
  const allowed = allowedOrigins === '*' || allowedOrigins.split(',').map(o => o.trim()).includes(origin);
  if (allowed) {
    headers['Access-Control-Allow-Origin'] = origin || '*';
    headers['Access-Control-Allow-Methods'] = 'POST, OPTIONS';
    headers['Access-Control-Allow-Headers'] = 'Content-Type';
  }

  return new Response(JSON.stringify(body), { status, headers });
}
