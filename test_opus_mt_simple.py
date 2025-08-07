#!/usr/bin/env python3
"""
OPUS-MT シンプルテストスクリプト
問題の特定と動作確認用
"""

import sys
import warnings
warnings.filterwarnings("ignore")

try:
    print("Step 1: Basic imports...", file=sys.stderr)
    import torch
    import transformers
    print(f"PyTorch version: {torch.__version__}", file=sys.stderr)
    print(f"Transformers version: {transformers.__version__}", file=sys.stderr)
    
    print("Step 2: Import specific classes...", file=sys.stderr)
    from transformers import AutoTokenizer, AutoModelForSeq2SeqLM
    print("AutoTokenizer and AutoModelForSeq2SeqLM imported successfully", file=sys.stderr)
    
    print("Step 3: Try loading tokenizer...", file=sys.stderr)
    model_name = "Helsinki-NLP/opus-mt-ja-en"
    tokenizer = AutoTokenizer.from_pretrained(model_name)
    print("Tokenizer loaded successfully", file=sys.stderr)
    
    print("Step 4: Try loading model...", file=sys.stderr)
    model = AutoModelForSeq2SeqLM.from_pretrained(model_name)
    print("Model loaded successfully", file=sys.stderr)
    
    print("Step 5: Test translation...", file=sys.stderr)
    input_text = "Game Update"
    inputs = tokenizer(input_text, return_tensors="pt")
    outputs = model.generate(**inputs, max_length=50, num_beams=3)
    translation = tokenizer.decode(outputs[0], skip_special_tokens=True)
    
    print(f"SUCCESS: '{input_text}' -> '{translation}'", file=sys.stderr)
    print(f'"{translation}"')  # JSON output for C#
    
except Exception as e:
    print(f"ERROR at step: {e}", file=sys.stderr)
    print(f'"{{"error": "{str(e)}"}}"')
    sys.exit(1)