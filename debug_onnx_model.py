#!/usr/bin/env python3
"""
ONNX モデルの入力・出力仕様を確認するスクリプト
"""

import onnx
import sys
import os

def print_model_info(model_path):
    """ONNXモデルの詳細情報を出力"""
    if not os.path.exists(model_path):
        print(f"❌ モデルファイルが見つかりません: {model_path}")
        return
    
    try:
        model = onnx.load(model_path)
        print(f"🔍 モデル解析: {model_path}")
        print("=" * 60)
        
        # 入力情報
        print("📥 入力:")
        for i, input_info in enumerate(model.graph.input):
            name = input_info.name
            type_info = input_info.type.tensor_type
            elem_type = type_info.elem_type
            shape = [dim.dim_value if dim.dim_value > 0 else dim.dim_param for dim in type_info.shape.dim]
            print(f"  {i+1}. {name}: {shape} (type: {elem_type})")
        
        # 出力情報
        print("\n📤 出力:")
        for i, output_info in enumerate(model.graph.output):
            name = output_info.name
            type_info = output_info.type.tensor_type
            elem_type = type_info.elem_type
            shape = [dim.dim_value if dim.dim_value > 0 else dim.dim_param for dim in type_info.shape.dim]
            print(f"  {i+1}. {name}: {shape} (type: {elem_type})")
        
        print("\n" + "=" * 60)
        
    except Exception as e:
        print(f"❌ エラー: {e}")

if __name__ == "__main__":
    # OPUS-MT モデルファイルのパス
    model_paths = [
        r"E:\dev\Baketa\Models\ONNX\opus-mt-ja-en.onnx"
    ]
    
    for model_path in model_paths:
        print_model_info(model_path)
        print()