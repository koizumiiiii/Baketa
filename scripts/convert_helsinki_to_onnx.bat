@echo off
echo === Helsinki-NLP OPUS-MT to ONNX Converter ===
echo.

cd /d "E:\dev\Baketa"

REM Check Python installation
echo Checking Python installation...
python --version > nul 2>&1
if %errorlevel% neq 0 (
    echo Error: Python is not installed or not in PATH
    echo Please install Python 3.8+ and add to PATH
    pause
    exit /b 1
)

REM Install required packages
echo Installing required packages...
pip install torch transformers onnx optimum[onnxruntime] --quiet

REM Create conversion script
echo Creating ONNX conversion script...
(
echo import torch
echo import onnx
echo from transformers import AutoTokenizer, AutoModelForSeq2SeqLM
echo import os
echo import sys
echo.
echo def convert_to_onnx^(model_path, output_path^):
echo     print^(f"Loading model from: {model_path}"^)
echo     
echo     try:
echo         # Load model and tokenizer
echo         tokenizer = AutoTokenizer.from_pretrained^(model_path^)
echo         model = AutoModelForSeq2SeqLM.from_pretrained^(model_path^)
echo         model.eval^(^)
echo         
echo         print^("Model loaded successfully"^)
echo         
echo         # Create dummy inputs
echo         sample_text = "これはテストです。"
echo         inputs = tokenizer^(sample_text, return_tensors="pt", padding=True^)
echo         
echo         input_ids = inputs.input_ids
echo         attention_mask = inputs.attention_mask
echo         
echo         # Create decoder input
echo         decoder_input_ids = torch.full^(^(1, 1^), tokenizer.pad_token_id, dtype=torch.long^)
echo         
echo         print^(f"Input shape: {input_ids.shape}"^)
echo         print^(f"Converting to ONNX format..."^)
echo         
echo         # Export to ONNX
echo         torch.onnx.export^(
echo             model,
echo             args=^(input_ids, attention_mask, decoder_input_ids^),
echo             f=output_path,
echo             export_params=True,
echo             opset_version=11,
echo             do_constant_folding=True,
echo             input_names=['input_ids', 'attention_mask', 'decoder_input_ids'],
echo             output_names=['output'],
echo             dynamic_axes={
echo                 'input_ids': {0: 'batch_size', 1: 'sequence_length'},
echo                 'attention_mask': {0: 'batch_size', 1: 'sequence_length'},
echo                 'decoder_input_ids': {0: 'batch_size', 1: 'decoder_sequence_length'},
echo                 'output': {0: 'batch_size', 1: 'decoder_sequence_length', 2: 'vocab_size'}
echo             }
echo         ^)
echo         
echo         print^(f"ONNX model saved to: {output_path}"^)
echo         
echo         # Verify ONNX model
echo         onnx_model = onnx.load^(output_path^)
echo         onnx.checker.check_model^(onnx_model^)
echo         print^("ONNX model validation passed"^)
echo         
echo         return True
echo         
echo     except Exception as e:
echo         print^(f"Error during conversion: {e}"^)
echo         return False
echo.
echo if __name__ == "__main__":
echo     model_path = "Models/HuggingFace/opus-mt-ja-en"
echo     output_path = "Models/ONNX/helsinki-opus-mt-ja-en.onnx"
echo     
echo     print^("=== Helsinki-NLP OPUS-MT to ONNX Converter ==="^)
echo     print^(f"Model path: {model_path}"^)
echo     print^(f"Output path: {output_path}"^)
echo     
echo     # Create output directory
echo     os.makedirs^(os.path.dirname^(output_path^), exist_ok=True^)
echo     
echo     success = convert_to_onnx^(model_path, output_path^)
echo     
echo     if success:
echo         print^("\n✅ Conversion completed successfully!"^)
echo         print^(f"ONNX model is available at: {output_path}"^)
echo         
echo         if os.path.exists^(output_path^):
echo             size_mb = os.path.getsize^(output_path^) / ^(1024 * 1024^)
echo             print^(f"File size: {size_mb:.2f} MB"^)
echo     else:
echo         print^("\n❌ Conversion failed!"^)
echo         sys.exit^(1^)
) > temp_convert.py

REM Run conversion
echo Running ONNX conversion...
python temp_convert.py

REM Cleanup
del temp_convert.py

echo.
echo Conversion process completed.
pause