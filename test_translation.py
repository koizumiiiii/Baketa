import sys
import os
sys.path.insert(0, 'scripts')
import opus_mt_service

# Test translation
result = opus_mt_service.translate_text("……複雑でよくわからない")
print(f"Result: {result}")