#!/usr/bin/env python3
"""
Python翻訳サーバー診断スクリプト
メモリ使用量、モデルロード時間、依存関係を詳細分析
"""

import asyncio
import json
import logging
import os
import time
import tracemalloc
import gc
from pathlib import Path
import psutil
import sys

# PyTorch/Transformers import test
try:
    import torch
    print(f"[OK] PyTorch {torch.__version__} loaded successfully")
    print(f"   Device available: {torch.device('cuda' if torch.cuda.is_available() else 'cpu')}")
    print(f"   CUDA available: {torch.cuda.is_available()}")
except ImportError as e:
    print(f"[ERROR] PyTorch import failed: {e}")
    sys.exit(1)

try:
    from transformers import MarianMTModel, MarianTokenizer
    print(f"[OK] Transformers loaded successfully")
except ImportError as e:
    print(f"[ERROR] Transformers import failed: {e}")
    sys.exit(1)

# ロギング設定
logging.basicConfig(level=logging.INFO, format='%(asctime)s - %(levelname)s - %(message)s')
logger = logging.getLogger(__name__)

class TranslationServerDiagnostics:
    """翻訳サーバー診断クラス"""
    
    def __init__(self):
        self.process = psutil.Process()
        self.models = {}
        self.device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
        
        # メモリトレース開始
        tracemalloc.start()
        
    def get_memory_info(self) -> dict:
        """メモリ使用量情報を取得"""
        memory_info = self.process.memory_info()
        return {
            "rss_mb": memory_info.rss / 1024 / 1024,  # Resident Set Size
            "vms_mb": memory_info.vms / 1024 / 1024,  # Virtual Memory Size
            "percent": self.process.memory_percent(),
            "available_mb": psutil.virtual_memory().available / 1024 / 1024,
            "total_mb": psutil.virtual_memory().total / 1024 / 1024
        }
    
    def log_memory_usage(self, step: str):
        """メモリ使用量をログ出力"""
        memory = self.get_memory_info()
        logger.info(f"[{step}] Memory - RSS: {memory['rss_mb']:.1f}MB, "
                   f"VMS: {memory['vms_mb']:.1f}MB, "
                   f"Percent: {memory['percent']:.1f}%, "
                   f"Available: {memory['available_mb']:.1f}MB")
        
        # PyTorchメモリ使用量（CUDA）
        if torch.cuda.is_available():
            allocated = torch.cuda.memory_allocated() / 1024**2
            cached = torch.cuda.memory_reserved() / 1024**2
            logger.info(f"[{step}] GPU Memory - Allocated: {allocated:.1f}MB, Cached: {cached:.1f}MB")
    
    def check_model_cache(self, model_name: str) -> bool:
        """HuggingFaceキャッシュ内のモデル存在確認"""
        cache_dir = Path.home() / ".cache" / "huggingface" / "transformers"
        if not cache_dir.exists():
            return False
            
        # モデル名からキャッシュディレクトリを推測
        model_dirs = list(cache_dir.glob("*"))
        for model_dir in model_dirs:
            if model_name.replace("/", "--") in str(model_dir):
                logger.info(f"[OK] Model cache found: {model_dir}")
                return True
        
        logger.warning(f"[WARN] Model cache not found for: {model_name}")
        return False
    
    async def test_model_loading(self, model_name: str, model_key: str) -> bool:
        """モデルロードテスト（タイムアウト付き）"""
        logger.info(f"[LOADING] Loading model: {model_name}")
        start_time = time.time()
        self.log_memory_usage(f"Before {model_key}")
        
        try:
            # タイムアウト付きモデルロード
            loop = asyncio.get_event_loop()
            
            def load_model():
                tokenizer = MarianTokenizer.from_pretrained(model_name)
                model = MarianMTModel.from_pretrained(model_name).to(self.device)
                model.eval()
                return model, tokenizer
            
            # 5分タイムアウト
            model, tokenizer = await asyncio.wait_for(
                loop.run_in_executor(None, load_model),
                timeout=300
            )
            
            self.models[model_key] = (model, tokenizer)
            
            load_time = time.time() - start_time
            self.log_memory_usage(f"After {model_key}")
            logger.info(f"[SUCCESS] Model loaded successfully: {model_key} ({load_time:.1f}s)")
            
            # ガベージコレクション
            gc.collect()
            if torch.cuda.is_available():
                torch.cuda.empty_cache()
                
            return True
            
        except asyncio.TimeoutError:
            logger.error(f"[TIMEOUT] Model loading timeout: {model_name} (>300s)")
            return False
        except Exception as e:
            logger.error(f"[ERROR] Model loading failed: {model_name} - {e}")
            return False
    
    async def test_translation(self, text: str, source_lang: str, target_lang: str) -> bool:
        """翻訳テスト"""
        model_key = f"{source_lang}-{target_lang}"
        if model_key not in self.models:
            logger.error(f"[ERROR] Model not loaded: {model_key}")
            return False
        
        try:
            start_time = time.time()
            model, tokenizer = self.models[model_key]
            
            # トークナイズ
            inputs = tokenizer(text, return_tensors="pt", padding=True, truncation=True, max_length=512)
            inputs = {k: v.to(self.device) for k, v in inputs.items()}
            
            # 推論
            with torch.no_grad():
                outputs = model.generate(**inputs, max_length=512, num_beams=1, early_stopping=True)
            
            # デコード
            translation = tokenizer.decode(outputs[0], skip_special_tokens=True)
            
            processing_time = (time.time() - start_time) * 1000
            logger.info(f"[SUCCESS] Translation successful: '{text}' -> '{translation}' ({processing_time:.1f}ms)")
            
            return True
            
        except Exception as e:
            logger.error(f"[ERROR] Translation failed: {e}")
            return False
    
    async def run_diagnostics(self):
        """診断実行"""
        logger.info("[START] Starting Translation Server Diagnostics")
        
        # 初期状態
        self.log_memory_usage("Initial")
        
        # システム情報
        logger.info(f"System Info - CPU: {psutil.cpu_count()}, "
                   f"RAM: {psutil.virtual_memory().total / 1024**3:.1f}GB")
        
        # モデルキャッシュ確認
        models_to_test = [
            ("Helsinki-NLP/opus-mt-ja-en", "ja-en"),
            ("Helsinki-NLP/opus-mt-en-jap", "en-ja")
        ]
        
        cache_status = {}
        for model_name, model_key in models_to_test:
            cache_status[model_key] = self.check_model_cache(model_name)
        
        # モデルロードテスト
        load_results = {}
        for model_name, model_key in models_to_test:
            logger.info(f"\n{'='*50}")
            logger.info(f"Testing model: {model_key}")
            logger.info(f"{'='*50}")
            
            success = await self.test_model_loading(model_name, model_key)
            load_results[model_key] = success
            
            if not success:
                logger.error(f"[STOP] Stopping diagnostics due to model load failure: {model_key}")
                break
                
            # メモリ使用量チェック
            memory = self.get_memory_info()
            if memory["percent"] > 85:
                logger.warning(f"[WARN] High memory usage: {memory['percent']:.1f}%")
        
        # 翻訳テスト
        if all(load_results.values()):
            logger.info(f"\n{'='*50}")
            logger.info("Testing translations")
            logger.info(f"{'='*50}")
            
            test_cases = [
                ("こんにちは", "ja", "en"),
                ("Hello", "en", "ja"),
                ("今日は良い天気です", "ja", "en"),
                ("How are you?", "en", "ja")
            ]
            
            for text, src, tgt in test_cases:
                await self.test_translation(text, src, tgt)
        
        # 最終メモリ使用量
        self.log_memory_usage("Final")
        
        # サマリー
        logger.info(f"\n{'='*50}")
        logger.info("DIAGNOSTICS SUMMARY")
        logger.info(f"{'='*50}")
        
        for model_key, cached in cache_status.items():
            cache_status_text = "[OK]" if cached else "[ERROR]"
            logger.info(f"Cache {model_key}: {cache_status_text}")
            
        for model_key, loaded in load_results.items():
            load_status_text = "[OK]" if loaded else "[ERROR]"
            logger.info(f"Load {model_key}: {load_status_text}")
        
        final_memory = self.get_memory_info()
        logger.info(f"Final Memory: {final_memory['rss_mb']:.1f}MB ({final_memory['percent']:.1f}%)")
        
        # 推奨事項
        logger.info(f"\n{'='*50}")
        logger.info("RECOMMENDATIONS")
        logger.info(f"{'='*50}")
        
        if not all(cache_status.values()):
            logger.info("[RECOMMEND] Run model pre-download: python -c \"from transformers import MarianMTModel, MarianTokenizer; MarianMTModel.from_pretrained('Helsinki-NLP/opus-mt-ja-en'); MarianMTModel.from_pretrained('Helsinki-NLP/opus-mt-en-jap')\"")
            
        if final_memory["percent"] > 70:
            logger.warning("[RECOMMEND] Consider increasing system RAM or using model quantization")
            
        if not all(load_results.values()):
            logger.error("[RECOMMEND] Model loading failed - check internet connection and disk space")

async def main():
    """メイン実行関数"""
    diagnostics = TranslationServerDiagnostics()
    await diagnostics.run_diagnostics()

if __name__ == "__main__":
    asyncio.run(main())