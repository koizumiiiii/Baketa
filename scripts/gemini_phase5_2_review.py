#!/usr/bin/env python3
"""Phase 5.2改訂版分析報告書のGeminiレビュースクリプト"""
import os
import google.generativeai as genai

# Gemini API設定
api_key = os.environ.get("GEMINI_API_KEY")
if not api_key:
    print("エラー: GEMINI_API_KEY環境変数が設定されていません")
    exit(1)

genai.configure(api_key=api_key)
model = genai.GenerativeModel("gemini-1.5-pro")

# プロンプトと改訂版報告書を読み込み
with open("docs/refactoring/PHASE5.2_GEMINI_REVIEW_PROMPT.txt", "r", encoding="utf-8") as f:
    prompt = f.read()

with open("docs/refactoring/PHASE5.2_REVISED_ANALYSIS.md", "r", encoding="utf-8") as f:
    report = f.read()

# レビューリクエスト送信
full_prompt = f"{prompt}\n\n---\n\n## 改訂版分析報告書全文\n\n{report}"
print("Gemini APIにレビューリクエストを送信中...")
print("=" * 80)

response = model.generate_content(full_prompt)
print(response.text)
