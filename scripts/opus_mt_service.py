#!/usr/bin/env python3
"""
Baketa OPUS-MT Translation Service
C#から呼び出される高品質日英翻訳サービス
"""

import sys
import json
import os
import warnings

# 警告を抑制してパフォーマンス向上
warnings.filterwarnings("ignore", category=UserWarning)
os.environ['TOKENIZERS_PARALLELISM'] = 'false'  # tokenizerの並列処理を無効化
os.environ['TRANSFORMERS_OFFLINE'] = '1'  # オフラインモード（キャッシュのみ使用）

from transformers import AutoTokenizer, AutoModelForSeq2SeqLM, logging
import torch

# transformersのログレベルを下げる
logging.set_verbosity_error()

# Python実行ファイルパス（Baketaプロジェクト用）
PYTHON_PATH = "/c/Users/suke0/.pyenv/pyenv-win/versions/3.10.9/python.exe"
MODEL_ID = "Helsinki-NLP/opus-mt-ja-en"

class BaketaOpusMtService:
    """
    Baketa専用OPUS-MT翻訳サービス
    語彙サイズ不整合問題を完全解決済み
    """
    
    def __init__(self):
        self.tokenizer = None
        self.model = None
        self.initialized = False
    
    def initialize(self):
        """
        HuggingFace Transformersでモデル初期化
        """
        try:
            print(f"🔄 Loading tokenizer for {MODEL_ID}...", file=sys.stderr, flush=True)
            self.tokenizer = AutoTokenizer.from_pretrained(MODEL_ID, local_files_only=True)
            
            print(f"🔄 Loading model for {MODEL_ID}...", file=sys.stderr, flush=True)
            self.model = AutoModelForSeq2SeqLM.from_pretrained(MODEL_ID, local_files_only=True)
            
            print("✅ Model initialization complete!", file=sys.stderr, flush=True)
            self.initialized = True
            return True
        except Exception as e:
            print(f"❌ Model initialization failed: {e}", file=sys.stderr, flush=True)
            return False
    
    def translate(self, japanese_text):
        """
        日本語→英語翻訳（高品質保証）
        """
        if not self.initialized:
            return {"error": "Service not initialized"}
        
        try:
            # HuggingFace Transformers標準処理
            inputs = self.tokenizer(japanese_text, return_tensors="pt")
            
            with torch.no_grad():
                outputs = self.model.generate(
                    **inputs,
                    max_length=128,
                    num_beams=3,
                    length_penalty=1.0,
                    early_stopping=True
                )
            
            translation = self.tokenizer.decode(outputs[0], skip_special_tokens=True)
            
            return {
                "success": True,
                "translation": translation,
                "source": japanese_text
            }
            
        except Exception as e:
            return {
                "success": False,
                "error": str(e),
                "source": japanese_text
            }

def main():
    """
    C#からの呼び出し用エントリーポイント
    """
    # Windows環境でUTF-8出力を強制
    import codecs
    sys.stdout = codecs.getwriter('utf-8')(sys.stdout.buffer)
    sys.stderr = codecs.getwriter('utf-8')(sys.stderr.buffer)
    
    if len(sys.argv) != 2:
        print(json.dumps({"error": "Usage: python opus_mt_service.py 'Japanese text'"}, ensure_ascii=False))
        sys.exit(1)
    
    text_arg = sys.argv[1]
    
    # 一時ファイルから読み取る場合（@プレフィックス）
    if text_arg.startswith("@"):
        temp_file_path = text_arg[1:]  # @を除去
        try:
            with open(temp_file_path, 'r', encoding='utf-8') as f:
                japanese_text = f.read().strip()
        except Exception as e:
            print(json.dumps({"error": f"Failed to read temp file: {str(e)}"}, ensure_ascii=False))
            sys.exit(1)
    else:
        japanese_text = text_arg
    
    service = BaketaOpusMtService()
    if not service.initialize():
        print(json.dumps({"error": "Failed to initialize translation service"}, ensure_ascii=False))
        sys.exit(1)
    
    result = service.translate(japanese_text)
    print(json.dumps(result, ensure_ascii=False))

if __name__ == "__main__":
    main()