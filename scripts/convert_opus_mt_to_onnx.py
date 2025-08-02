#!/usr/bin/env python3
"""
Helsinki-NLP OPUS-MT モデルをONNX形式に変換するスクリプト
"""

import torch
import onnx
from transformers import AutoTokenizer, AutoModelForSeq2SeqLM
import os
import sys

def convert_opus_mt_to_onnx(model_path, output_path):
    """
    OPUS-MTモデルをONNX形式に変換
    
    Args:
        model_path: HuggingFaceモデルのパス
        output_path: 出力ONNXファイルのパス
    """
    
    print(f"Loading model from: {model_path}")
    
    # モデルとトークナイザーの読み込み
    try:
        tokenizer = AutoTokenizer.from_pretrained(model_path)
        model = AutoModelForSeq2SeqLM.from_pretrained(model_path)
        model.eval()
        
        print("Model loaded successfully")
        
        # ダミー入力の作成（日本語のサンプルテキスト）
        sample_text = "これはテストです。"
        inputs = tokenizer(sample_text, return_tensors="pt", padding=True)
        
        input_ids = inputs.input_ids
        attention_mask = inputs.attention_mask
        
        print(f"Input shape: {input_ids.shape}")
        print(f"Attention mask shape: {attention_mask.shape}")
        
        # デコーダーの初期入力（BOSトークン）
        decoder_input_ids = torch.full((1, 1), tokenizer.pad_token_id, dtype=torch.long)
        
        # 動的な軸を定義
        dynamic_axes = {
            'input_ids': {0: 'batch_size', 1: 'sequence_length'},
            'attention_mask': {0: 'batch_size', 1: 'sequence_length'},
            'decoder_input_ids': {0: 'batch_size', 1: 'decoder_sequence_length'},
            'output': {0: 'batch_size', 1: 'decoder_sequence_length', 2: 'vocab_size'}
        }
        
        # ONNX変換実行
        print("Converting to ONNX format...")
        torch.onnx.export(
            model,
            args=(input_ids, attention_mask, decoder_input_ids),
            f=output_path,
            export_params=True,
            opset_version=11,
            do_constant_folding=True,
            input_names=['input_ids', 'attention_mask', 'decoder_input_ids'],
            output_names=['output'],
            dynamic_axes=dynamic_axes
        )
        
        print(f"ONNX model saved to: {output_path}")
        
        # ONNXモデルの検証
        onnx_model = onnx.load(output_path)
        onnx.checker.check_model(onnx_model)
        print("ONNX model validation passed")
        
        return True
        
    except Exception as e:
        print(f"Error during conversion: {e}")
        return False

def main():
    """メイン処理"""
    
    # モデルのパス設定
    base_dir = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    model_path = os.path.join(base_dir, "Models", "HuggingFace", "opus-mt-ja-en")
    output_path = os.path.join(base_dir, "Models", "ONNX", "helsinki-opus-mt-ja-en.onnx")
    
    print("=== Helsinki-NLP OPUS-MT to ONNX Converter ===")
    print(f"Model path: {model_path}")
    print(f"Output path: {output_path}")
    
    # 出力ディレクトリの作成
    os.makedirs(os.path.dirname(output_path), exist_ok=True)
    
    # 変換実行
    success = convert_opus_mt_to_onnx(model_path, output_path)
    
    if success:
        print("\n✅ Conversion completed successfully!")
        print(f"ONNX model is available at: {output_path}")
        
        # ファイルサイズ確認
        if os.path.exists(output_path):
            size_mb = os.path.getsize(output_path) / (1024 * 1024)
            print(f"File size: {size_mb:.2f} MB")
            
    else:
        print("\n❌ Conversion failed!")
        sys.exit(1)

if __name__ == "__main__":
    main()