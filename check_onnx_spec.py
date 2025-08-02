#!/usr/bin/env python3

import onnx

# Decoder Model Analysis
print("=== ONNX-Community Decoder Model Analysis ===")
try:
    decoder_model = onnx.load("Models/ONNX/onnx-community-decoder_model.onnx")
    print("Decoder Inputs:")
    for input_tensor in decoder_model.graph.input:
        print(f"  - {input_tensor.name}: {input_tensor.type}")
    print("Decoder Outputs:")
    for output_tensor in decoder_model.graph.output:
        print(f"  - {output_tensor.name}: {output_tensor.type}")
    print()
except Exception as e:
    print(f"Decoder model error: {e}")

# Encoder Model Analysis
print("=== ONNX-Community Encoder Model Analysis ===")
try:
    encoder_model = onnx.load("Models/ONNX/onnx-community-encoder_model.onnx")
    print("Encoder Inputs:")
    for input_tensor in encoder_model.graph.input:
        print(f"  - {input_tensor.name}: {input_tensor.type}")
    print("Encoder Outputs:")
    for output_tensor in encoder_model.graph.output:
        print(f"  - {output_tensor.name}: {output_tensor.type}")
    print()
except Exception as e:
    print(f"Encoder model error: {e}")

# Original Model Analysis for comparison
print("=== Original helsinki-opus-mt-ja-en.onnx Analysis ===")
try:
    original_model = onnx.load("Models/ONNX/helsinki-opus-mt-ja-en.onnx")
    print("Original Inputs:")
    for input_tensor in original_model.graph.input:
        print(f"  - {input_tensor.name}: {input_tensor.type}")
    print("Original Outputs:")
    for output_tensor in original_model.graph.output:
        print(f"  - {output_tensor.name}: {output_tensor.type}")
    print()
except Exception as e:
    print(f"Original model error: {e}")