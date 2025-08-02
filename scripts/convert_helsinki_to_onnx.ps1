param()

Write-Host "=== Helsinki-NLP OPUS-MT to ONNX Converter ===" -ForegroundColor Green
Write-Host ""

Set-Location "E:\dev\Baketa"

# Check Python installation
Write-Host "Checking Python installation..." -ForegroundColor Yellow
try {
    $pythonVersion = & python --version 2>&1
    Write-Host "Python found: $pythonVersion" -ForegroundColor Green
} catch {
    Write-Host "Error: Python is not installed or not in PATH" -ForegroundColor Red
    Write-Host "Please install Python 3.8+ and add to PATH" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

# Install required packages
Write-Host "Installing required packages..." -ForegroundColor Yellow
try {
    & pip install torch transformers onnx optimum[onnxruntime] --quiet
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Package installation failed, continuing anyway..." -ForegroundColor Yellow
    } else {
        Write-Host "Packages installed successfully" -ForegroundColor Green
    }
} catch {
    Write-Host "Package installation error, continuing anyway..." -ForegroundColor Yellow
}

# Create Python conversion script
$pythonScript = @"
import torch
import onnx
from transformers import AutoTokenizer, AutoModelForSeq2SeqLM
import os
import sys

def convert_to_onnx(model_path, output_path):
    print(f"Loading model from: {model_path}")
    
    try:
        # Load model and tokenizer
        tokenizer = AutoTokenizer.from_pretrained(model_path)
        model = AutoModelForSeq2SeqLM.from_pretrained(model_path)
        model.eval()
        
        print("Model loaded successfully")
        
        # Create dummy inputs
        sample_text = "これはテストです。"
        inputs = tokenizer(sample_text, return_tensors="pt", padding=True)
        
        input_ids = inputs.input_ids
        attention_mask = inputs.attention_mask
        
        # Create decoder input
        decoder_input_ids = torch.full((1, 1), tokenizer.pad_token_id, dtype=torch.long)
        
        print(f"Input shape: {input_ids.shape}")
        print(f"Converting to ONNX format...")
        
        # Export to ONNX
        torch.onnx.export(
            model,
            args=(input_ids, attention_mask, decoder_input_ids),
            f=output_path,
            export_params=True,
            opset_version=11,
            do_constant_folding=True,
            input_names=['input_ids', 'attention_mask', 'decoder_input_ids'],
            output_names=['output'],
            dynamic_axes={
                'input_ids': {0: 'batch_size', 1: 'sequence_length'},
                'attention_mask': {0: 'batch_size', 1: 'sequence_length'},
                'decoder_input_ids': {0: 'batch_size', 1: 'decoder_sequence_length'},
                'output': {0: 'batch_size', 1: 'decoder_sequence_length', 2: 'vocab_size'}
            }
        )
        
        print(f"ONNX model saved to: {output_path}")
        
        # Verify ONNX model
        onnx_model = onnx.load(output_path)
        onnx.checker.check_model(onnx_model)
        print("ONNX model validation passed")
        
        return True
        
    except Exception as e:
        print(f"Error during conversion: {e}")
        import traceback
        traceback.print_exc()
        return False

if __name__ == "__main__":
    model_path = "Models/HuggingFace/opus-mt-ja-en"
    output_path = "Models/ONNX/helsinki-opus-mt-ja-en.onnx"
    
    print("=== Helsinki-NLP OPUS-MT to ONNX Converter ===")
    print(f"Model path: {model_path}")
    print(f"Output path: {output_path}")
    
    # Create output directory
    os.makedirs(os.path.dirname(output_path), exist_ok=True)
    
    success = convert_to_onnx(model_path, output_path)
    
    if success:
        print("\n✅ Conversion completed successfully!")
        print(f"ONNX model is available at: {output_path}")
        
        if os.path.exists(output_path):
            size_mb = os.path.getsize(output_path) / (1024 * 1024)
            print(f"File size: {size_mb:.2f} MB")
    else:
        print("\n❌ Conversion failed!")
        sys.exit(1)
"@

# Write Python script to temp file
$tempScript = "temp_convert.py"
$pythonScript | Out-File -FilePath $tempScript -Encoding UTF8

# Run conversion
Write-Host "Running ONNX conversion..." -ForegroundColor Yellow
try {
    & python $tempScript
    if ($LASTEXITCODE -eq 0) {
        Write-Host "Conversion completed successfully!" -ForegroundColor Green
    } else {
        Write-Host "Conversion failed with exit code: $LASTEXITCODE" -ForegroundColor Red
    }
} catch {
    Write-Host "Error executing conversion script: $_" -ForegroundColor Red
} finally {
    # Cleanup
    if (Test-Path $tempScript) {
        Remove-Item $tempScript
    }
}

Write-Host ""
Write-Host "Conversion process completed." -ForegroundColor Green
Read-Host "Press Enter to continue"