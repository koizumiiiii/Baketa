#!/usr/bin/env python3
"""Gemini APIに質問を送信するスクリプト"""
import os
import sys
import google.generativeai as genai

# UTF-8出力設定
sys.stdout.reconfigure(encoding='utf-8')
sys.stderr.reconfigure(encoding='utf-8')

# Gemini API設定
api_key = os.environ.get("GEMINI_API_KEY")
if not api_key:
    print("エラー: GEMINI_API_KEY環境変数が設定されていません")
    exit(1)

genai.configure(api_key=api_key)
model = genai.GenerativeModel("models/gemini-2.0-flash-exp")

# プロンプトファイルを読み込み
if len(sys.argv) < 2:
    print("使用方法: python ask_gemini.py <prompt_file>")
    exit(1)

prompt_file = sys.argv[1]
if not os.path.exists(prompt_file):
    print(f"エラー: {prompt_file} が見つかりません")
    exit(1)

with open(prompt_file, "r", encoding="utf-8") as f:
    prompt = f.read()

# Geminiにリクエスト送信
print("=" * 80)
print("Gemini 2.0 Flash Expにリクエストを送信中...")
print("=" * 80)
print()

try:
    response = model.generate_content(prompt)
    print(response.text)
    print()
    print("=" * 80)
    print("完了")
    print("=" * 80)
except Exception as e:
    print(f"エラー: Gemini APIリクエスト失敗 - {e}", file=sys.stderr)
    exit(1)
