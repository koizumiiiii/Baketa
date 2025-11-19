#!/usr/bin/env python3
"""利用可能なGeminiモデルをリストアップ"""
import os
import sys
import google.generativeai as genai

sys.stdout.reconfigure(encoding='utf-8')

api_key = os.environ.get("GEMINI_API_KEY")
if not api_key:
    print("エラー: GEMINI_API_KEY環境変数が設定されていません")
    exit(1)

genai.configure(api_key=api_key)

print("利用可能なGeminiモデル:")
print("=" * 80)
for model in genai.list_models():
    if 'generateContent' in model.supported_generation_methods:
        print(f"モデル名: {model.name}")
        print(f"  表示名: {model.display_name}")
        print(f"  サポート: {', '.join(model.supported_generation_methods)}")
        print()
