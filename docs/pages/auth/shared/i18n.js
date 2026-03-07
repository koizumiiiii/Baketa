/**
 * Shared internationalization utilities for Baketa auth pages
 */

/** Supported languages for auth pages */
var SUPPORTED_LANGUAGES = ['ja', 'en', 'ko', 'zh-CN', 'zh-TW', 'fr', 'de', 'it', 'es', 'pt'];

/**
 * Get language from URL parameter (query string or hash fragment) or browser
 * @returns {string} The detected language code
 */
function getLanguage() {
    // Check query string first (?lang=en)
    const params = new URLSearchParams(window.location.search);
    const langParam = params.get('lang');
    if (langParam && SUPPORTED_LANGUAGES.includes(langParam)) {
        return langParam;
    }
    // Also check hash fragment (Supabase puts params after #)
    const hashParams = new URLSearchParams(window.location.hash.substring(1));
    const hashLang = hashParams.get('lang');
    if (hashLang && SUPPORTED_LANGUAGES.includes(hashLang)) {
        return hashLang;
    }
    // Fallback to browser language
    const browserLang = navigator.language || navigator.userLanguage;
    if (SUPPORTED_LANGUAGES.includes(browserLang)) {
        return browserLang;
    }
    // Prefix match (e.g., 'fr-FR' -> 'fr', 'ko-KR' -> 'ko')
    const prefix = browserLang.split('-')[0];
    if (prefix === 'zh') {
        return (browserLang.includes('TW') || browserLang.includes('Hant')) ? 'zh-TW' : 'zh-CN';
    }
    var match = SUPPORTED_LANGUAGES.find(function(l) { return l === prefix; });
    if (match) return match;
    return 'en';
}

/**
 * Apply translations to page elements
 * @param {Object} translations - Object with language keys containing translation strings
 * @param {Object} elementMap - Object mapping translation keys to element IDs
 */
function applyTranslations(translations, elementMap) {
    const lang = getLanguage();
    const t = translations[lang] || translations['en'];

    document.documentElement.lang = lang;

    for (const [key, elementId] of Object.entries(elementMap)) {
        const element = document.getElementById(elementId);
        if (element && t[key]) {
            // Use innerHTML for keys that may contain HTML (like 'message')
            if (key === 'message' || t[key].includes('<')) {
                element.innerHTML = t[key];
            } else {
                element.textContent = t[key];
            }
        }
    }
}
