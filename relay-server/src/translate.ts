/**
 * Cloud AI翻訳エンドポイント
 * Gemini / OpenAI APIを使用した画像テキスト翻訳
 *
 * 対応プロバイダー:
 * - Gemini (gemini-2.5-flash-lite)
 * - OpenAI (gpt-4.1-nano)
 *
 * セキュリティ:
 * - セッショントークン検証（Patreon認証済みユーザーのみ）
 * - プランチェック（Pro/Premiaのみ利用可能）
 * - レートリミット
 */

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
  plan: 'Free' | 'Standard' | 'Pro' | 'Premia';
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
}

// ============================================
// 定数
// ============================================

const GEMINI_API_BASE = 'https://generativelanguage.googleapis.com/v1beta';
const DEFAULT_GEMINI_MODEL = 'gemini-2.5-flash-lite';
const OPENAI_API_BASE = 'https://api.openai.com/v1';
const DEFAULT_OPENAI_MODEL = 'gpt-4.1-nano';
const IDENTITY_CACHE_TTL_SECONDS = 5 * 60; // 5 minutes
const API_TIMEOUT_MS = 30000; // 30秒タイムアウト

/** Cloud AI翻訳が利用可能なプラン */
const ALLOWED_PLANS = ['Pro', 'Premia'] as const;

// ============================================
// ヘルパー関数
// ============================================

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

/**
 * AIレスポンスからJSONをパース
 * マークダウンコードブロック（```json...```）を除去してパース
 */
function parseAiJsonResponse(textContent: string): { detected_text?: string; translated_text?: string; detected_language?: string } | null {
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
    return JSON.parse(jsonText);
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
  tokenUsage?: { input: number; output: number; image: number };
  error?: { code: string; message: string; isRetryable: boolean };
}> {
  const contextHint = request.context ? `\nContext: This is from a ${request.context}.` : '';
  const sourceHint = request.source_language !== 'auto'
    ? `The source language is ${request.source_language}.`
    : 'Detect the source language.';

  const prompt = `You are a translation assistant for game UI text.${contextHint}

Task: Extract all visible text from this image and translate it to ${request.target_language}.
${sourceHint}

Response format (JSON only, no markdown):
{
  "detected_text": "original text from image",
  "translated_text": "translation in ${request.target_language}",
  "detected_language": "ISO 639-1 code of source language"
}

If no text is visible, respond with:
{
  "detected_text": "",
  "translated_text": "",
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
      maxOutputTokens: 1024,
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
  tokenUsage?: { input: number; output: number; image: number };
  error?: { code: string; message: string; isRetryable: boolean };
}> {
  const contextHint = request.context ? `\nContext: This is from a ${request.context}.` : '';
  const sourceHint = request.source_language !== 'auto'
    ? `The source language is ${request.source_language}.`
    : 'Detect the source language.';

  const systemPrompt = `You are a translation assistant for game UI text. Always respond with valid JSON only, no markdown formatting.`;

  const userPrompt = `${contextHint}

Task: Extract all visible text from this image and translate it to ${request.target_language}.
${sourceHint}

Response format (JSON only, no markdown):
{
  "detected_text": "original text from image",
  "translated_text": "translation in ${request.target_language}",
  "detected_language": "ISO 639-1 code of source language"
}

If no text is visible, respond with:
{
  "detected_text": "",
  "translated_text": "",
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
    max_tokens: 1024,
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

  // セッション検証
  const session = await getSession(env, sessionToken);
  if (!session) {
    return errorResponse(defaultRequestId, 'SESSION_INVALID', 'Invalid or expired session', false, 401, origin, allowedOrigins);
  }

  if (Date.now() > session.expiresAt) {
    return errorResponse(defaultRequestId, 'SESSION_INVALID', 'Session expired', false, 401, origin, allowedOrigins);
  }

  // プランチェック
  const cachedMembership = await getCachedMembership(env, session.userId);
  if (!cachedMembership) {
    return errorResponse(defaultRequestId, 'SESSION_INVALID', 'Membership information not found', false, 401, origin, allowedOrigins);
  }

  const plan = cachedMembership.membership.plan;
  if (!ALLOWED_PLANS.includes(plan as typeof ALLOWED_PLANS[number])) {
    return errorResponse(
      defaultRequestId,
      'PLAN_NOT_SUPPORTED',
      `Cloud AI translation requires Pro or Premia plan. Current plan: ${plan}`,
      false,
      403,
      origin,
      allowedOrigins
    );
  }

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
    };

    console.log(`Translate success: requestId=${requestId}, userId=${session.userId}, plan=${plan}, provider=gemini, tokens=${result.tokenUsage?.input || 0}+${result.tokenUsage?.output || 0}`);

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
    };

    console.log(`Translate success: requestId=${requestId}, userId=${session.userId}, plan=${plan}, provider=openai, tokens=${result.tokenUsage?.input || 0}+${result.tokenUsage?.output || 0}`);

    return successResponse(response, origin, allowedOrigins);
  }

  return errorResponse(requestId, 'VALIDATION_ERROR', 'Invalid provider', false, 400, origin, allowedOrigins);
}
