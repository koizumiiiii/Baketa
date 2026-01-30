/**
 * Shared internationalization utilities for Baketa auth pages
 */

/**
 * Get language from URL parameter (query string or hash fragment) or browser
 * @returns {'ja' | 'en'} The detected language
 */
function getLanguage() {
    // Check query string first (?lang=en)
    const params = new URLSearchParams(window.location.search);
    const langParam = params.get('lang');
    if (langParam && (langParam === 'ja' || langParam === 'en')) {
        return langParam;
    }
    // Also check hash fragment (Supabase puts params after #)
    const hashParams = new URLSearchParams(window.location.hash.substring(1));
    const hashLang = hashParams.get('lang');
    if (hashLang && (hashLang === 'ja' || hashLang === 'en')) {
        return hashLang;
    }
    // Fallback to browser language
    const browserLang = navigator.language || navigator.userLanguage;
    return browserLang.startsWith('ja') ? 'ja' : 'en';
}

/**
 * Apply translations to page elements
 * @param {Object} translations - Object with 'ja' and 'en' keys containing translation strings
 * @param {Object} elementMap - Object mapping translation keys to element IDs
 */
function applyTranslations(translations, elementMap) {
    const lang = getLanguage();
    const t = translations[lang];

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
