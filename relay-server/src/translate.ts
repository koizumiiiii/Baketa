/**
 * Cloud AI翻訳エンドポイント
 * Gemini / OpenAI APIを使用した画像テキスト翻訳
 *
 * 対応プロバイダー:
 * - Gemini (gemini-2.5-flash-lite)
 * - OpenAI (gpt-4.1-nano)
 *
 * セキュリティ:
 * - [Issue #280+#281] Supabase JWT認証（Patreonセッション or Supabase JWT）
 * - プランチェック（Pro/Premium/Ultimate または ボーナストークン所有者）
 * - レートリミット
 */

import { createClient, SupabaseClient } from '@supabase/supabase-js';
import { jwtVerify, JWTPayload } from 'jose';

// ============================================
// 型定義
// ============================================

export interface TranslateEnv {
  SESSIONS: KVNamespace;
  GEMINI_API_KEY?: string;
  OPENAI_API_KEY?: string;
  GEMINI_MODEL?: string;  // モデル名（デフォルト: gemini-2.5-flash-lite）
  OPENAI_MODEL?: string;  // モデル名（デフォルト: gpt-4.1-nano）
  API_KEY: string;
  ALLOWED_ORIGINS: string;
  ENVIRONMENT?: string;
  // [Issue #280+#281] Supabase認証用
  SUPABASE_URL?: string;
  SUPABASE_SERVICE_KEY?: string;
  // [Issue #287] JWT認証用
  JWT_SECRET?: string;
}

/** セッションデータ（index.tsと共有） */
interface SessionData {
  accessToken: string;
  refreshToken: string;
  expiresAt: number;
  userId: string;
}

/** キャッシュ済みメンバーシップ情報 */
interface CachedMembership {
  membership: ParsedMembership;
  cachedAt: number;
}

/** パース済みメンバーシップ情報 */
interface ParsedMembership {
  userId: string;
  email: string;
  fullName: string;
  plan: PlanType;  // Issue #257
  tierId: string;
  patronStatus: string;
  nextChargeDate: string | null;
  entitledAmountCents: number;
}

/** 翻訳リクエスト */
interface TranslateRequest {
  provider: 'gemini' | 'openai';
  image_base64: string;
  mime_type: string;
  source_language: string;
  target_language: string;
  context?: string;
  request_id?: string;
}

/** Gemini API リクエスト */
interface GeminiRequest {
  contents: Array<{
    parts: Array<{
      text?: string;
      inlineData?: {
        mimeType: string;
        data: string;
      };
    }>;
  }>;
  generationConfig?: {
    temperature?: number;
    maxOutputTokens?: number;
  };
}

/** Gemini API レスポンス */
interface GeminiResponse {
  candidates?: Array<{
    content?: {
      parts?: Array<{
        text?: string;
      }>;
    };
    finishReason?: string;
  }>;
  usageMetadata?: {
    promptTokenCount?: number;
    candidatesTokenCount?: number;
    totalTokenCount?: number;
  };
  error?: {
    code: number;
    message: string;
    status: string;
  };
}

/** OpenAI API リクエスト */
interface OpenAIRequest {
  model: string;
  messages: Array<{
    role: 'system' | 'user' | 'assistant';
    content: string | Array<{
      type: 'text' | 'image_url';
      text?: string;
      image_url?: {
        url: string;
        detail?: 'low' | 'high' | 'auto';
      };
    }>;
  }>;
  max_tokens?: number;
  temperature?: number;
}

/** OpenAI API レスポンス */
interface OpenAIResponse {
  id: string;
  object: string;
  created: number;
  model: string;
  choices: Array<{
    index: number;
    message: {
      role: string;
      content: string;
    };
    finish_reason: string;
  }>;
  usage?: {
    prompt_tokens: number;
    completion_tokens: number;
    total_tokens: number;
  };
  error?: {
    message: string;
    type: string;
    code: string;
  };
}

/** 翻訳レスポンス */
interface TranslateResponse {
  success: boolean;
  request_id: string;
  detected_text?: string;
  translated_text?: string;
  detected_language?: string;
  provider_id?: string;
  token_usage?: {
    input_tokens: number;
    output_tokens: number;
    image_tokens: number;
  };
  processing_time_ms?: number;
  error?: {
    code: string;
    message: string;
    is_retryable: boolean;
  };
  /** [Issue #275] 複数テキスト対応 - BoundingBox付きテキスト配列 */
  texts?: TranslatedTextItem[];
}

/** [Issue #275] 翻訳されたテキストアイテム */
interface TranslatedTextItem {
  original: string;
  translation: string;
  /** BoundingBox座標 [y_min, x_min, y_max, x_max] (0-1000スケール) */
  bounding_box?: [number, number, number, number];
}

// ============================================
// 定数
// ============================================

const GEMINI_API_BASE = 'https://generativelanguage.googleapis.com/v1beta';
const DEFAULT_GEMINI_MODEL = 'gemini-2.5-flash-lite';
const OPENAI_API_BASE = 'https://api.openai.com/v1';
const DEFAULT_OPENAI_MODEL = 'gpt-4.1-nano';
/** [Issue #286] メンバーシップキャッシュTTL（1時間）- 手動同期で即時反映可能 */
const IDENTITY_CACHE_TTL_SECONDS = 60 * 60; // 1 hour (was 5 minutes)
const API_TIMEOUT_MS = 30000; // 30秒タイムアウト
/** [Issue #286] 認証結果キャッシュTTL（1時間）- KV Write削減のため延長 */
const AUTH_CACHE_TTL_SECONDS = 60 * 60; // 1 hour (was 60 seconds)

// [Issue #287] JWT認証設定
const JWT_ISSUER = 'https://baketa-relay.suke009.workers.dev';
const JWT_AUDIENCE = 'baketa-client';

/** [Issue #257] プラン名定義 */
const PLAN = {
  FREE: 'Free',
  PRO: 'Pro',
  PREMIUM: 'Premium',
  ULTIMATE: 'Ultimate',
} as const;

/** プラン型 */
type PlanType = typeof PLAN[keyof typeof PLAN];

/** Cloud AI翻訳が利用可能なプラン */
// Issue #257: Pro/Premium/Ultimate 3段階構成に改定
const ALLOWED_PLANS: readonly PlanType[] = [PLAN.PRO, PLAN.PREMIUM, PLAN.ULTIMATE];

// ============================================
// [Issue #280+#281] 認証結果型
// ============================================

/** 認証済みユーザー情報 */
interface AuthenticatedUser {
  userId: string;
  plan: PlanType;
  hasBonusTokens: boolean;
  authMethod: 'patreon' | 'supabase' | 'jwt';  // [Issue #287] JWT認証追加
}

/** [Issue #287] JWTカスタムクレーム */
interface BaketaJwtClaims extends JWTPayload {
  sub: string;           // Patreon user ID
  plan: PlanType;        // 現在のプラン
  hasBonusTokens: boolean; // ボーナストークン有無
  authMethod: 'patreon'; // 元の認証方法
}

// ============================================
// ヘルパー関数
// ============================================

// --------------------------------------------
// [Issue #287] JWT認証ヘルパー
// --------------------------------------------

/**
 * [Issue #287] JWTアクセストークンを検証
 * @param env 環境変数
 * @param token JWTトークン
 * @returns 検証結果（成功時はペイロード、失敗時はnull）
 */
async function validateJwtToken(
  env: TranslateEnv,
  token: string
): Promise<BaketaJwtClaims | null> {
  if (!env.JWT_SECRET) {
    // JWT_SECRETが未設定の場合はJWT認証をスキップ
    return null;
  }

  try {
    const secret = new TextEncoder().encode(env.JWT_SECRET);
    const { payload } = await jwtVerify(token, secret, {
      issuer: JWT_ISSUER,
      audience: JWT_AUDIENCE,
    });

    // 必須クレームの検証
    if (!payload.sub || typeof payload.plan !== 'string') {
      console.warn('[Issue #287] JWT missing required claims');
      return null;
    }

    return payload as BaketaJwtClaims;
  } catch (error) {
    // JWTとして無効な場合は他の認証方式にフォールバック
    // エラーログは出力しない（SessionTokenの可能性があるため）
    return null;
  }
}

// --------------------------------------------
// [Issue #280+#281] Supabase認証ヘルパー
// --------------------------------------------

/** Supabaseクライアント取得 */
function getSupabaseClient(env: TranslateEnv): SupabaseClient | null {
  if (!env.SUPABASE_URL || !env.SUPABASE_SERVICE_KEY) {
    return null;
  }
  return createClient(env.SUPABASE_URL, env.SUPABASE_SERVICE_KEY, {
    auth: { persistSession: false }
  });
}

/** ユーザーのボーナストークン残高を取得 */
async function getBonusTokensRemaining(supabase: SupabaseClient, userId: string): Promise<number> {
  try {
    const { data, error } = await supabase.rpc('get_bonus_tokens_for_user', {
      p_user_id: userId
    });

    if (error || !Array.isArray(data)) {
      console.error('[Issue #280+#281] Bonus tokens RPC error:', error);
      return 0;
    }

    // 有効なボーナス（未期限切れで残高あり）の合計を計算
    return data
      .filter((b: { is_expired: boolean; remaining_tokens: number }) =>
        !b.is_expired && b.remaining_tokens > 0)
      .reduce((sum: number, b: { remaining_tokens: number }) => sum + b.remaining_tokens, 0);
  } catch (error) {
    console.error('[Issue #280+#281] Bonus tokens check failed:', error);
    return 0;
  }
}

/**
 * [Issue #280+#281] 統合認証
 * [Issue #287] JWT認証を最優先に追加
 * 1. Baketa JWT認証を試行（最優先）
 * 2. キャッシュを確認
 * 3. Patreonセッション（KV）を試行
 * 4. 失敗したらSupabase JWT認証を試行
 * 5. ボーナストークンの有無も確認
 * 6. 結果をキャッシュに保存
 */
async function authenticateUser(
  env: TranslateEnv,
  sessionToken: string
): Promise<AuthenticatedUser | null> {
  // [Issue #287] 1. Baketa JWT認証を試行（最優先）
  const jwtClaims = await validateJwtToken(env, sessionToken);
  if (jwtClaims && jwtClaims.sub) {
    console.log(`[Issue #287] JWT auth success: userId=${jwtClaims.sub.substring(0, 8)}..., plan=${jwtClaims.plan}`);
    return {
      userId: jwtClaims.sub,
      plan: jwtClaims.plan,
      hasBonusTokens: jwtClaims.hasBonusTokens,
      authMethod: 'jwt',
    };
  }

  // トークンのハッシュをキーに使用（セキュリティ対策）
  const tokenHash = await hashToken(sessionToken);
  const cacheKey = `auth:${tokenHash}`;

  // 2. キャッシュを確認（[Issue #286] Cache APIを使用 - KV制限回避）
  const cachedAuth = await getAuthCache(cacheKey);
  if (cachedAuth) {
    console.log(`[Issue #286] Auth cache hit (Cache API): userId=${cachedAuth.userId.substring(0, 8)}...`);
    return cachedAuth;
  }

  // 3. Patreonセッション（KV）を試行
  const patreonSession = await getSession(env, sessionToken);
  if (patreonSession && Date.now() <= patreonSession.expiresAt) {
    const cachedMembership = await getCachedMembership(env, patreonSession.userId);
    if (cachedMembership) {
      const result: AuthenticatedUser = {
        userId: patreonSession.userId,
        plan: cachedMembership.membership.plan,
        hasBonusTokens: false, // Patreonユーザーはボーナス不要（プランで判定）
        authMethod: 'patreon'
      };
      // キャッシュに保存（[Issue #286] Cache API使用）
      await saveAuthCache(cacheKey, result);
      return result;
    }
  }

  // 4. Supabase JWT認証を試行
  const supabase = getSupabaseClient(env);
  if (!supabase) {
    console.log('[Issue #280+#281] Supabase not configured, skipping JWT auth');
    return null;
  }

  try {
    const { data: { user }, error } = await supabase.auth.getUser(sessionToken);
    if (error || !user) {
      console.log('[Issue #280+#281] Supabase JWT validation failed:', error?.message);
      return null;
    }

    // 4. ボーナストークン残高を確認
    const bonusRemaining = await getBonusTokensRemaining(supabase, user.id);
    const hasBonusTokens = bonusRemaining > 0;

    console.log(`[Issue #280+#281] Supabase auth success: userId=${user.id.substring(0, 8)}..., bonusTokens=${bonusRemaining}`);

    const result: AuthenticatedUser = {
      userId: user.id,
      plan: PLAN.FREE, // SupabaseユーザーはFreeプラン（ボーナストークンで利用）
      hasBonusTokens,
      authMethod: 'supabase'
    };

    // 5. キャッシュに保存（[Issue #286] Cache API使用）
    await saveAuthCache(cacheKey, result);
    return result;
  } catch (error) {
    console.error('[Issue #280+#281] Supabase auth error:', error);
    return null;
  }
}

/** [Issue #280+#281] トークンをハッシュ化（キャッシュキー用） */
async function hashToken(token: string): Promise<string> {
  const encoder = new TextEncoder();
  const data = encoder.encode(token);
  const hashBuffer = await crypto.subtle.digest('SHA-256', data);
  const hashArray = Array.from(new Uint8Array(hashBuffer));
  return hashArray.slice(0, 16).map(b => b.toString(16).padStart(2, '0')).join('');
}

// ============================================
// [Issue #286] Cache API ヘルパー関数
// KV制限を回避するためCache APIを使用（無制限）
// ============================================

/** Cache APIのキーとなるURLを生成 */
function getCacheUrl(cacheKey: string): string {
  return `https://baketa-auth-cache.internal/${cacheKey}`;
}

/** [Issue #286] 認証キャッシュを読み取り（Cache API） */
async function getAuthCache(cacheKey: string): Promise<AuthenticatedUser | null> {
  try {
    const cache = caches.default;
    const cacheRequest = new Request(getCacheUrl(cacheKey));
    const cachedResponse = await cache.match(cacheRequest);

    if (cachedResponse) {
      const data = await cachedResponse.json<AuthenticatedUser>();
      return data;
    }
    return null;
  } catch {
    return null;
  }
}

/** [Issue #286] 認証結果をキャッシュに保存（Cache API） */
async function saveAuthCache(cacheKey: string, auth: AuthenticatedUser): Promise<void> {
  try {
    const cache = caches.default;
    const cacheRequest = new Request(getCacheUrl(cacheKey));
    const cacheResponse = new Response(JSON.stringify(auth), {
      headers: {
        'Content-Type': 'application/json',
        'Cache-Control': `max-age=${AUTH_CACHE_TTL_SECONDS}`,
      },
    });

    await cache.put(cacheRequest, cacheResponse);
    console.log(`[Issue #286] Auth cached (Cache API): key=${cacheKey.substring(0, 12)}...`);
  } catch (error) {
    // キャッシュ保存エラーは無視（翻訳処理を続行）
    console.warn('[Issue #286] Auth cache save failed:', error);
  }
}

// --------------------------------------------
// 既存ヘルパー関数
// --------------------------------------------

function corsHeaders(origin: string, allowedOrigins: string): Record<string, string> {
  const allowed = allowedOrigins.split(',').map(o => o.trim());
  const isAllowed = allowed.includes('*') || allowed.includes(origin);

  return {
    'Access-Control-Allow-Origin': isAllowed ? origin : (allowed[0] || ''),
    'Access-Control-Allow-Methods': 'GET, POST, OPTIONS',
    'Access-Control-Allow-Headers': 'Content-Type, Authorization, X-API-Key',
    'Access-Control-Max-Age': '86400',
  };
}

function errorResponse(
  requestId: string,
  code: string,
  message: string,
  isRetryable: boolean,
  status: number,
  origin: string,
  allowedOrigins: string,
  processingTimeMs?: number
): Response {
  const response: TranslateResponse = {
    success: false,
    request_id: requestId,
    processing_time_ms: processingTimeMs,
    error: { code, message, is_retryable: isRetryable },
  };
  return new Response(JSON.stringify(response), {
    status,
    headers: { 'Content-Type': 'application/json', ...corsHeaders(origin, allowedOrigins) },
  });
}

function successResponse(
  data: TranslateResponse,
  origin: string,
  allowedOrigins: string
): Response {
  return new Response(JSON.stringify(data), {
    status: 200,
    headers: { 'Content-Type': 'application/json', ...corsHeaders(origin, allowedOrigins) },
  });
}

async function getSession(env: TranslateEnv, sessionToken: string): Promise<SessionData | null> {
  try {
    const data = await env.SESSIONS.get<SessionData>(sessionToken, 'json');
    if (data && typeof data.accessToken === 'string' && typeof data.expiresAt === 'number') {
      return data;
    }
    return null;
  } catch {
    return null;
  }
}

async function getCachedMembership(env: TranslateEnv, userId: string): Promise<CachedMembership | null> {
  try {
    const cacheKey = `membership:${userId}`;
    const data = await env.SESSIONS.get<CachedMembership>(cacheKey, 'json');
    if (data && data.membership && typeof data.cachedAt === 'number') {
      if (Date.now() - data.cachedAt < IDENTITY_CACHE_TTL_SECONDS * 1000) {
        return data;
      }
    }
    return null;
  } catch {
    return null;
  }
}

function validateTranslateRequest(body: unknown): { valid: true; data: TranslateRequest } | { valid: false; error: string } {
  if (!body || typeof body !== 'object') {
    return { valid: false, error: 'Invalid request body' };
  }

  const obj = body as Record<string, unknown>;

  if (typeof obj.provider !== 'string' || !['gemini', 'openai'].includes(obj.provider)) {
    return { valid: false, error: 'Invalid or missing provider (must be "gemini" or "openai")' };
  }

  if (typeof obj.image_base64 !== 'string' || !obj.image_base64) {
    return { valid: false, error: 'Missing or invalid image_base64' };
  }

  if (typeof obj.mime_type !== 'string' || !obj.mime_type) {
    return { valid: false, error: 'Missing or invalid mime_type' };
  }

  if (typeof obj.target_language !== 'string' || !obj.target_language) {
    return { valid: false, error: 'Missing or invalid target_language' };
  }

  return {
    valid: true,
    data: {
      provider: obj.provider as 'gemini' | 'openai',
      image_base64: obj.image_base64,
      mime_type: obj.mime_type,
      source_language: typeof obj.source_language === 'string' ? obj.source_language : 'auto',
      target_language: obj.target_language,
      context: typeof obj.context === 'string' ? obj.context : undefined,
      request_id: typeof obj.request_id === 'string' ? obj.request_id : crypto.randomUUID(),
    },
  };
}

/**
 * 画像サイズからトークン数を推定（概算値）
 *
 * 注意: Gemini Visionのトークン数は実際にはピクセル数に基づいて計算されますが、
 * クライアント側では画像のピクセル情報がBase64から直接取得できないため、
 * ファイルサイズからの概算値を使用しています。
 *
 * Gemini Vision: 258トークン（~768px）～ 1032トークン（~3072px）
 */
function estimateImageTokens(base64Length: number): number {
  // Base64は約1.33倍のサイズなので、元のバイト数を推定
  const estimatedBytes = Math.floor(base64Length * 0.75);
  // 1MB = 約500トークンと推定（概算値）
  return Math.max(258, Math.min(1032, Math.floor(estimatedBytes / 2000)));
}

/** [Issue #275] AIレスポンスのパース結果型 */
interface ParsedAiResponse {
  detected_text?: string;
  translated_text?: string;
  detected_language?: string;
  /** [Issue #275] 複数テキスト対応 */
  texts?: Array<{
    original: string;
    translation: string;
    bounding_box?: [number, number, number, number];
  }>;
}

/**
 * AIレスポンスからJSONをパース
 * マークダウンコードブロック（```json...```）を除去してパース
 * [Issue #275] 複数テキスト形式にも対応
 */
function parseAiJsonResponse(textContent: string): ParsedAiResponse | null {
  let jsonText = textContent.trim();

  // マークダウンコードブロックを除去
  if (jsonText.startsWith('```json')) {
    jsonText = jsonText.slice(7);
  } else if (jsonText.startsWith('```')) {
    jsonText = jsonText.slice(3);
  }
  if (jsonText.endsWith('```')) {
    jsonText = jsonText.slice(0, -3);
  }
  jsonText = jsonText.trim();

  try {
    const parsed = JSON.parse(jsonText);

    // [Issue #275] 新形式（texts配列）の場合、後方互換性のためdetected_text/translated_textも設定
    if (parsed.texts && Array.isArray(parsed.texts) && parsed.texts.length > 0) {
      const firstText = parsed.texts[0];
      return {
        detected_text: firstText.original || '',
        translated_text: firstText.translation || '',
        detected_language: parsed.detected_language,
        texts: parsed.texts,
      };
    }

    // 旧形式の場合はそのまま返す
    return parsed;
  } catch {
    return null;
  }
}

// ============================================
// Gemini API 呼び出し
// ============================================

async function translateWithGemini(
  request: TranslateRequest,
  apiKey: string,
  modelName: string = DEFAULT_GEMINI_MODEL
): Promise<{
  success: boolean;
  detectedText?: string;
  translatedText?: string;
  detectedLanguage?: string;
  /** [Issue #275] 複数テキスト対応 */
  texts?: Array<{ original: string; translation: string; bounding_box?: [number, number, number, number] }>;
  tokenUsage?: { input: number; output: number; image: number };
  error?: { code: string; message: string; isRetryable: boolean };
}> {
  const contextHint = request.context ? `\nContext: This is from a ${request.context}.` : '';
  const sourceHint = request.source_language !== 'auto'
    ? `The source language is ${request.source_language}.`
    : 'Detect the source language.';

  // [Issue #275] 複数テキスト+BoundingBox形式のプロンプト（DirectGeminiImageTranslatorと同等）
  const prompt = `You are a game localization expert. Detect ALL visible text in this image and translate it to ${request.target_language}.${contextHint}

## Translation Guidelines
- Use natural, fluent expressions in ${request.target_language}, not literal translations
- Maintain appropriate tone for game UI and dialog
- Keep proper nouns (character names, place names) as-is or use standard transliteration
- Choose appropriate formality level based on context
- ${sourceHint}

## Output Format
Include ALL detected text items with their bounding box coordinates.
Bounding boxes use normalized 0-1000 scale coordinates in [y_min, x_min, y_max, x_max] order.

Response format (JSON only, no markdown):
{
  "texts": [
    {
      "original": "original text 1",
      "translation": "translated text 1",
      "bounding_box": [y_min, x_min, y_max, x_max]
    },
    {
      "original": "original text 2",
      "translation": "translated text 2",
      "bounding_box": [y_min, x_min, y_max, x_max]
    }
  ],
  "detected_language": "ISO 639-1 code (e.g., ja, en, ko)"
}

If no text is visible, respond with:
{
  "texts": [],
  "detected_language": ""
}`;

  const geminiRequest: GeminiRequest = {
    contents: [{
      parts: [
        { text: prompt },
        {
          inlineData: {
            mimeType: request.mime_type,
            data: request.image_base64,
          },
        },
      ],
    }],
    generationConfig: {
      temperature: 0.1,
      maxOutputTokens: 2048, // [Issue #275] 複数テキスト対応のため増加
    },
  };

  const url = `${GEMINI_API_BASE}/models/${modelName}:generateContent?key=${apiKey}`;

  try {
    const response = await fetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(geminiRequest),
      signal: AbortSignal.timeout(API_TIMEOUT_MS),
    });

    if (!response.ok) {
      const errorBody = await response.text();
      console.error(`Gemini API error: status=${response.status}, body=${errorBody}`);

      if (response.status === 429) {
        return {
          success: false,
          error: { code: 'RATE_LIMITED', message: 'Gemini API rate limit exceeded', isRetryable: true },
        };
      }

      if (response.status === 401 || response.status === 403) {
        return {
          success: false,
          error: { code: 'API_ERROR', message: 'Gemini API authentication failed', isRetryable: false },
        };
      }

      return {
        success: false,
        error: { code: 'API_ERROR', message: `Gemini API error: ${response.status}`, isRetryable: response.status >= 500 },
      };
    }

    const geminiResponse = await response.json() as GeminiResponse;

    if (geminiResponse.error) {
      return {
        success: false,
        error: { code: 'API_ERROR', message: geminiResponse.error.message, isRetryable: false },
      };
    }

    const textContent = geminiResponse.candidates?.[0]?.content?.parts?.[0]?.text;
    if (!textContent) {
      return {
        success: false,
        error: { code: 'API_ERROR', message: 'No content in Gemini response', isRetryable: false },
      };
    }

    // JSONをパース
    const parsed = parseAiJsonResponse(textContent);
    if (!parsed) {
      console.error(`Failed to parse Gemini response as JSON: ${textContent}`);
      return {
        success: false,
        error: { code: 'API_ERROR', message: 'Invalid JSON response from Gemini', isRetryable: false },
      };
    }

    const usage = geminiResponse.usageMetadata;

    return {
      success: true,
      detectedText: parsed.detected_text || '',
      translatedText: parsed.translated_text || '',
      detectedLanguage: parsed.detected_language,
      // [Issue #275] 複数テキスト対応
      texts: parsed.texts,
      tokenUsage: {
        input: usage?.promptTokenCount || 0,
        output: usage?.candidatesTokenCount || 0,
        image: estimateImageTokens(request.image_base64.length),
      },
    };
  } catch (error) {
    console.error('Gemini API call failed:', error);

    // タイムアウトエラーの判定
    if (error instanceof Error && error.name === 'TimeoutError') {
      return {
        success: false,
        error: { code: 'TIMEOUT', message: `Gemini API request timed out after ${API_TIMEOUT_MS}ms`, isRetryable: true },
      };
    }

    return {
      success: false,
      error: { code: 'NETWORK_ERROR', message: 'Failed to connect to Gemini API', isRetryable: true },
    };
  }
}

// ============================================
// OpenAI API 呼び出し
// ============================================

async function translateWithOpenAI(
  request: TranslateRequest,
  apiKey: string,
  modelName: string = DEFAULT_OPENAI_MODEL
): Promise<{
  success: boolean;
  detectedText?: string;
  translatedText?: string;
  detectedLanguage?: string;
  /** [Issue #275] 複数テキスト対応 */
  texts?: Array<{ original: string; translation: string; bounding_box?: [number, number, number, number] }>;
  tokenUsage?: { input: number; output: number; image: number };
  error?: { code: string; message: string; isRetryable: boolean };
}> {
  const contextHint = request.context ? `\nContext: This is from a ${request.context}.` : '';
  const sourceHint = request.source_language !== 'auto'
    ? `The source language is ${request.source_language}.`
    : 'Detect the source language.';

  // [Issue #275] 複数テキスト+BoundingBox形式のプロンプト
  const systemPrompt = `You are a game localization expert. Always respond with valid JSON only, no markdown formatting.`;

  const userPrompt = `${contextHint}

Task: Detect ALL visible text in this image and translate it to ${request.target_language}.
${sourceHint}

## Translation Guidelines
- Use natural, fluent expressions in ${request.target_language}, not literal translations
- Maintain appropriate tone for game UI and dialog
- Keep proper nouns (character names, place names) as-is or use standard transliteration
- Choose appropriate formality level based on context

## Output Format
Include ALL detected text items with their bounding box coordinates.
Bounding boxes use normalized 0-1000 scale coordinates in [y_min, x_min, y_max, x_max] order.

Response format (JSON only, no markdown):
{
  "texts": [
    {
      "original": "original text 1",
      "translation": "translated text 1",
      "bounding_box": [y_min, x_min, y_max, x_max]
    }
  ],
  "detected_language": "ISO 639-1 code (e.g., ja, en, ko)"
}

If no text is visible, respond with:
{
  "texts": [],
  "detected_language": ""
}`;

  const openaiRequest: OpenAIRequest = {
    model: modelName,
    messages: [
      { role: 'system', content: systemPrompt },
      {
        role: 'user',
        content: [
          { type: 'text', text: userPrompt },
          {
            type: 'image_url',
            image_url: {
              url: `data:${request.mime_type};base64,${request.image_base64}`,
              detail: 'high',
            },
          },
        ],
      },
    ],
    max_tokens: 2048, // [Issue #275] 複数テキスト対応のため増加
    temperature: 0.1,
  };

  const url = `${OPENAI_API_BASE}/chat/completions`;

  try {
    const response = await fetch(url, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${apiKey}`,
      },
      body: JSON.stringify(openaiRequest),
      signal: AbortSignal.timeout(API_TIMEOUT_MS),
    });

    if (!response.ok) {
      const errorBody = await response.text();
      console.error(`OpenAI API error: status=${response.status}, body=${errorBody}`);

      if (response.status === 429) {
        return {
          success: false,
          error: { code: 'RATE_LIMITED', message: 'OpenAI API rate limit exceeded', isRetryable: true },
        };
      }

      if (response.status === 401 || response.status === 403) {
        return {
          success: false,
          error: { code: 'API_ERROR', message: 'OpenAI API authentication failed', isRetryable: false },
        };
      }

      if (response.status === 402) {
        return {
          success: false,
          error: { code: 'PAYMENT_REQUIRED', message: 'OpenAI API credit exhausted', isRetryable: false },
        };
      }

      return {
        success: false,
        error: { code: 'API_ERROR', message: `OpenAI API error: ${response.status}`, isRetryable: response.status >= 500 },
      };
    }

    const openaiResponse = await response.json() as OpenAIResponse;

    if (openaiResponse.error) {
      return {
        success: false,
        error: { code: 'API_ERROR', message: openaiResponse.error.message, isRetryable: false },
      };
    }

    const textContent = openaiResponse.choices?.[0]?.message?.content;
    if (!textContent) {
      return {
        success: false,
        error: { code: 'API_ERROR', message: 'No content in OpenAI response', isRetryable: false },
      };
    }

    // JSONをパース
    const parsed = parseAiJsonResponse(textContent);
    if (!parsed) {
      console.error(`Failed to parse OpenAI response as JSON: ${textContent}`);
      return {
        success: false,
        error: { code: 'API_ERROR', message: 'Invalid JSON response from OpenAI', isRetryable: false },
      };
    }

    const usage = openaiResponse.usage;

    return {
      success: true,
      detectedText: parsed.detected_text || '',
      translatedText: parsed.translated_text || '',
      detectedLanguage: parsed.detected_language,
      // [Issue #275] 複数テキスト対応
      texts: parsed.texts,
      tokenUsage: {
        input: usage?.prompt_tokens || 0,
        output: usage?.completion_tokens || 0,
        image: estimateImageTokens(request.image_base64.length),
      },
    };
  } catch (error) {
    console.error('OpenAI API call failed:', error);

    // タイムアウトエラーの判定
    if (error instanceof Error && error.name === 'TimeoutError') {
      return {
        success: false,
        error: { code: 'TIMEOUT', message: `OpenAI API request timed out after ${API_TIMEOUT_MS}ms`, isRetryable: true },
      };
    }

    return {
      success: false,
      error: { code: 'NETWORK_ERROR', message: 'Failed to connect to OpenAI API', isRetryable: true },
    };
  }
}

// ============================================
// メインハンドラ
// ============================================

export async function handleTranslate(
  request: Request,
  env: TranslateEnv,
  origin: string,
  allowedOrigins: string
): Promise<Response> {
  const startTime = Date.now();
  const defaultRequestId = crypto.randomUUID();

  if (request.method !== 'POST') {
    return errorResponse(defaultRequestId, 'METHOD_NOT_ALLOWED', 'Method not allowed', false, 405, origin, allowedOrigins);
  }

  // セッショントークン取得
  const authHeader = request.headers.get('Authorization');
  if (!authHeader || !authHeader.startsWith('Bearer ')) {
    return errorResponse(defaultRequestId, 'SESSION_INVALID', 'Missing or invalid Authorization header', false, 401, origin, allowedOrigins);
  }

  const sessionToken = authHeader.substring(7);

  // [Issue #280+#281] 統合認証（Patreonセッション or Supabase JWT）
  const authenticatedUser = await authenticateUser(env, sessionToken);
  if (!authenticatedUser) {
    return errorResponse(defaultRequestId, 'SESSION_INVALID', 'Invalid or expired session', false, 401, origin, allowedOrigins);
  }

  // [Issue #280+#281] 利用権限チェック: 有料プラン or ボーナストークン所有者
  const isPaidPlan = ALLOWED_PLANS.includes(authenticatedUser.plan);
  if (!isPaidPlan && !authenticatedUser.hasBonusTokens) {
    return errorResponse(
      defaultRequestId,
      'PLAN_NOT_SUPPORTED',
      `Cloud AI translation requires a paid plan (Pro/Premium/Ultimate) or bonus tokens. Current plan: ${authenticatedUser.plan}`,
      false,
      403,
      origin,
      allowedOrigins
    );
  }

  const userId = authenticatedUser.userId;
  const plan = authenticatedUser.plan;

  // リクエストボディ検証
  let body: unknown;
  try {
    body = await request.json();
  } catch {
    return errorResponse(defaultRequestId, 'VALIDATION_ERROR', 'Invalid JSON body', false, 400, origin, allowedOrigins);
  }

  const validation = validateTranslateRequest(body);
  if (!validation.valid) {
    return errorResponse(defaultRequestId, 'VALIDATION_ERROR', validation.error, false, 400, origin, allowedOrigins);
  }

  const translateRequest = validation.data;
  const requestId = translateRequest.request_id || defaultRequestId;

  // プロバイダー別処理
  if (translateRequest.provider === 'gemini') {
    if (!env.GEMINI_API_KEY) {
      return errorResponse(requestId, 'NOT_IMPLEMENTED', 'Gemini API is not configured', false, 503, origin, allowedOrigins);
    }

    const modelName = env.GEMINI_MODEL || DEFAULT_GEMINI_MODEL;
    const result = await translateWithGemini(translateRequest, env.GEMINI_API_KEY, modelName);
    const processingTimeMs = Date.now() - startTime;

    if (!result.success) {
      return errorResponse(
        requestId,
        result.error!.code,
        result.error!.message,
        result.error!.isRetryable,
        result.error!.code === 'RATE_LIMITED' ? 429 : 500,
        origin,
        allowedOrigins,
        processingTimeMs
      );
    }

    const response: TranslateResponse = {
      success: true,
      request_id: requestId,
      detected_text: result.detectedText,
      translated_text: result.translatedText,
      detected_language: result.detectedLanguage,
      provider_id: 'gemini',
      token_usage: result.tokenUsage ? {
        input_tokens: result.tokenUsage.input,
        output_tokens: result.tokenUsage.output,
        image_tokens: result.tokenUsage.image,
      } : undefined,
      processing_time_ms: processingTimeMs,
      // [Issue #275] 複数テキスト対応
      texts: result.texts,
    };

    console.log(`Translate success: requestId=${requestId}, userId=${userId}, plan=${plan}, authMethod=${authenticatedUser.authMethod}, provider=gemini, texts=${result.texts?.length || 0}, tokens=${result.tokenUsage?.input || 0}+${result.tokenUsage?.output || 0}`);

    return successResponse(response, origin, allowedOrigins);
  }

  if (translateRequest.provider === 'openai') {
    if (!env.OPENAI_API_KEY) {
      return errorResponse(requestId, 'NOT_IMPLEMENTED', 'OpenAI API is not configured', false, 503, origin, allowedOrigins);
    }

    const modelName = env.OPENAI_MODEL || DEFAULT_OPENAI_MODEL;
    const result = await translateWithOpenAI(translateRequest, env.OPENAI_API_KEY, modelName);
    const processingTimeMs = Date.now() - startTime;

    if (!result.success) {
      return errorResponse(
        requestId,
        result.error!.code,
        result.error!.message,
        result.error!.isRetryable,
        result.error!.code === 'RATE_LIMITED' ? 429 : (result.error!.code === 'PAYMENT_REQUIRED' ? 402 : 500),
        origin,
        allowedOrigins,
        processingTimeMs
      );
    }

    const response: TranslateResponse = {
      success: true,
      request_id: requestId,
      detected_text: result.detectedText,
      translated_text: result.translatedText,
      detected_language: result.detectedLanguage,
      provider_id: 'openai',
      token_usage: result.tokenUsage ? {
        input_tokens: result.tokenUsage.input,
        output_tokens: result.tokenUsage.output,
        image_tokens: result.tokenUsage.image,
      } : undefined,
      processing_time_ms: processingTimeMs,
      // [Issue #275] 複数テキスト対応
      texts: result.texts,
    };

    console.log(`Translate success: requestId=${requestId}, userId=${userId}, plan=${plan}, authMethod=${authenticatedUser.authMethod}, provider=openai, texts=${result.texts?.length || 0}, tokens=${result.tokenUsage?.input || 0}+${result.tokenUsage?.output || 0}`);

    return successResponse(response, origin, allowedOrigins);
  }

  return errorResponse(requestId, 'VALIDATION_ERROR', 'Invalid provider', false, 400, origin, allowedOrigins);
}
