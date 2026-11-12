using System.Text.Json;
using Baketa.Core.Translation.Abstractions;

namespace Baketa.Infrastructure.Translation.Cloud;

internal static class GeminiPrompt
{
    // Ported from relay-server/src/translate.ts; coordinates retain the 0..1000 YXYX convention.
    public static string Create(ImageTranslationRequest request)
    {
        var sourceHint = request.SourceLanguage == "auto" ? "Detect the source language." : $"The source language is {request.SourceLanguage}.";
        var context = $"Context: {request.Context}\nPrevious translations (consistency only): {JsonSerializer.Serialize(request.TranslationHistory)}\nOCR hints: {JsonSerializer.Serialize(request.OcrHints)}";
        if (request.FocusRegion is { } r)
            context += FormattableString.Invariant($"\nONLY translate text within left {r.X * 100}%, top {r.Y * 100}%, right {(r.X + r.Width) * 100}%, bottom {(r.Y + r.Height) * 100}% of the image. Ignore ALL text outside this region.");
        var jaSpecific = request.TargetLanguage == "ja" ? "\nUse appropriate sentence-final particles. Omit pronouns when natural. Avoid translationese." : "";
        return $$"""
        # Role
        Expert game localizer. Output compact minified JSON ONLY. No pretty-printing.
        
        # Task
        Detect and translate ALL visible text in this image to {{request.TargetLanguage}}. {{sourceHint}}
        {{context}}
        Must include: Dialogs, UI buttons, menus, and all labels. Maximum 20 items.
        Do not invent or guess text that is not clearly visible in the image.
        
        # Guidelines
        - Creative localization, not word-for-word.
        - Preserve character tone/personality from visual context.
        - Keep proper nouns as-is.
        - CRITICAL: Within each text element, extract ONLY the readable characters without reproducing visual spacing or layout. If characters of a single word appear spread out, join them (e.g., "大　地" → "大地").
        - Character names and dialog text are SEPARATE items even if visually adjacent. A name label above/beside a dialog box is its own item.
        - When multiple lines form a continuous sentence (e.g., dialog spanning 2-3 lines within the same text box), combine them into ONE item. Do NOT split a sentence across multiple items.
        - [Issue #523] CRITICAL: ALL translations MUST be entirely in {{request.TargetLanguage}}. Do NOT mix in English or any other language. Only proper nouns (character names, place names) may remain in their original form.{{jaSpecific}}
        
        # Rules
        - Every item MUST have is_legible, original, and translation fields.
        - If text is clearly readable: set is_legible=true, fill original and translation normally.
        - If text is blurry, unreadable, or uses special fonts: set is_legible=false, original="" and translation="". NEVER pad with tabs, spaces, or guess characters.
        - If text is already in {{request.TargetLanguage}}, keep it as-is in "translation".
        - The "original" and "translation" fields must contain ONLY printable text characters. No whitespace padding, no tab characters, no control characters.
        - Include a confidence_score (0.0-1.0) for each translation indicating certainty.
        
        # Examples of correct extraction
        These examples show how to handle visual spacing and separate different text elements:
        
        Example - Game dialog scene with character name + dialog + UI button:
        Image shows a name label "大　地" above a dialog box "「　どうだ？　これなら　仲良く　兄妹で　同じ風呂　に入れるだろう？　」" and a button "セ　ー　ブ"
        Correct output (3 separate items):
        {"texts":[{"is_legible":true,"original":"大地","translation":"Daichi","bounding_box":[40,650,130,720],"confidence_score":0.98},{"is_legible":true,"original":"「どうだ？これなら仲良く兄妹で同じ風呂に入れるだろう？」","translation":"How about this? Now you siblings can share the bath together, right?","bounding_box":[600,50,750,950],"confidence_score":0.95},{"is_legible":true,"original":"セーブ","translation":"Save","bounding_box":[20,900,60,980],"confidence_score":0.97}],"detected_language":"ja"}
        Note: Name, dialog, and button are SEPARATE items. Visual spacing within each text is removed.
        
        # Output Format (minified JSON, no indentation or line breaks)
        {"texts":[{"is_legible":true,"original":"Dialog text","translation":"翻訳テキスト","bounding_box":[100,50,200,950],"confidence_score":0.95}],"detected_language":"ja"}
        
        Bounding boxes: normalized 0-1000 scale, [y_min, x_min, y_max, x_max].
        If no text visible: {"texts":[],"detected_language":""}
        """;
    }
}
