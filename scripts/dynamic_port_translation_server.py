#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Dynamic Port NLLB-200 Translation Server
Baketa PythonServerManager compatible version
"""

import argparse
import asyncio
import json
import logging
import signal
import sys
import time
import socket
import os
from threading import Thread
from concurrent.futures import ThreadPoolExecutor
from dataclasses import dataclass
from typing import Dict, Optional

# 文字エンコーディング設定 - Windows環境対応
if sys.platform.startswith('win'):
    os.environ['PYTHONIOENCODING'] = 'utf-8'
    import codecs
    sys.stdout = codecs.getwriter('utf-8')(sys.stdout.buffer, 'strict')
    sys.stderr = codecs.getwriter('utf-8')(sys.stderr.buffer, 'strict')

import torch
from transformers import AutoTokenizer, AutoModelForSeq2SeqLM

# ロギング設定
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(name)s - %(levelname)s - %(message)s'
)
logger = logging.getLogger(__name__)

# カスタム例外
class ModelNotLoadedError(Exception):
    pass

class UnsupportedLanguageError(Exception):
    pass

class ModelInferenceError(Exception):
    pass

@dataclass
class TranslationRequest:
    text: str
    source_lang: str = "en"
    target_lang: str = "ja"
    request_id: Optional[str] = None

@dataclass 
class TranslationResponse:
    translation: str
    processing_time: float
    source_lang: str
    target_lang: str
    success: bool = True
    error_message: Optional[str] = None

class DynamicNLLBTranslationServer:
    """Dynamic Port NLLB-200 Translation Server"""
    
    def __init__(self, port: int = 5000):
        self.port = port
        self.model = None
        self.tokenizer = None
        self.device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
        self.server_socket = None
        self.running = False
        self.executor = ThreadPoolExecutor(max_workers=4)
        
        # NLLB-200言語コードマッピング
        self.language_mapping = {
            "en": "eng_Latn",  # English
            "ja": "jpn_Jpan",  # Japanese
            "zh": "zho_Hans",  # Chinese (Simplified)
            "ko": "kor_Hang",  # Korean
            "es": "spa_Latn",  # Spanish
            "fr": "fra_Latn",  # French
            "de": "deu_Latn",  # German
            "ru": "rus_Cyrl",  # Russian
            "auto": "eng_Latn"  # Auto-detect fallback
        }
        
    def _get_nllb_lang_code(self, lang_code: str) -> str:
        """言語コードをNLLB-200形式に変換"""
        return self.language_mapping.get(lang_code.lower(), "eng_Latn")
        
    async def load_model(self):
        """NLLB-200モデルをロード"""
        try:
            logger.info(f"Using device: {self.device}")
            logger.info("NLLB-200モデルをロード中...")
            logger.info("🚀 NLLB_MODEL_LOAD_START: モデルロード開始")
            
            start_time = time.time()
            model_name = "facebook/nllb-200-distilled-600M"
            logger.info(f"モデル {model_name} をロード中...")
            
            # 高速ロード設定
            load_kwargs = {
                "torch_dtype": torch.float16 if self.device.type == "cuda" else torch.float32,
                "low_cpu_mem_usage": True,
                "local_files_only": False  # キャッシュ優先、必要時ダウンロード
            }
            
            # トークナイザーとモデルをロード
            logger.info("Tokenizer loading...")
            self.tokenizer = AutoTokenizer.from_pretrained(model_name, **load_kwargs)
            
            logger.info("Model loading...")
            self.model = AutoModelForSeq2SeqLM.from_pretrained(model_name, **load_kwargs)
            self.model.to(self.device)
            
            load_time = time.time() - start_time
            logger.info(f"NLLB-200モデルロード完了 - 所要時間: {load_time:.2f}秒")
            logger.info("🎉 NLLB_MODEL_LOAD_COMPLETE: モデルロード完了")
            
            # ウォームアップ
            logger.info("NLLB-200モデルウォームアップ開始...")
            await self.warmup()
            logger.info("🏁 NLLB_MODEL_READY: すべての準備完了")
            
        except Exception as e:
            logger.error(f"Model loading failed: {e}")
            raise ModelNotLoadedError(f"Failed to load NLLB-200 model: {e}")
    
    async def warmup(self):
        """モデルウォームアップ"""
        try:
            # 英語→日本語ウォームアップ
            logger.info("🔄 [NLLB-200] ウォームアップ: 'Hello...' [en->ja]")
            warmup_result = await self.translate_text("Hello, how are you?", "en", "ja")
            logger.info(f"英語→日本語ウォームアップ完了")
            
            # 日本語→英語ウォームアップ
            logger.info("🔄 [NLLB-200] ウォームアップ: 'こんにちは...' [ja->en]")
            warmup_result = await self.translate_text("こんにちは、元気ですか？", "ja", "en")
            logger.info("日本語→英語ウォームアップ完了")
            
        except Exception as e:
            logger.warning(f"Warmup warning (non-critical): {e}")
    
    async def translate_text(self, text: str, source_lang: str, target_lang: str) -> str:
        """テキスト翻訳実行"""
        if not self.model or not self.tokenizer:
            raise ModelNotLoadedError("Model not loaded")
        
        try:
            # 言語コード変換
            src_lang = self._get_nllb_lang_code(source_lang)
            tgt_lang = self._get_nllb_lang_code(target_lang)
            
            # 同言語チェック
            if src_lang == tgt_lang:
                return text  # 同じ言語の場合はそのまま返す
            
            # 言語設定
            self.tokenizer.src_lang = src_lang
            self.tokenizer.tgt_lang = tgt_lang
            
            # トークナイズ
            inputs = self.tokenizer(text, return_tensors="pt", padding=True, truncation=True, max_length=512)
            inputs = {k: v.to(self.device) for k, v in inputs.items()}
            
            # 推論
            with torch.no_grad():
                if self.device.type == "cuda":
                    with torch.cuda.amp.autocast():
                        outputs = self.model.generate(
                            **inputs,
                            forced_bos_token_id=self.tokenizer.convert_tokens_to_ids(tgt_lang),
                            max_length=512, 
                            num_beams=4, 
                            early_stopping=True
                        )
                else:
                    outputs = self.model.generate(
                        **inputs,
                        forced_bos_token_id=self.tokenizer.convert_tokens_to_ids(tgt_lang),
                        max_length=512, 
                        num_beams=4, 
                        early_stopping=True
                    )
            
            # デコード
            translation = self.tokenizer.decode(outputs[0], skip_special_tokens=True)
            logger.info(f"Translation: '{text[:50]}...' -> '{translation[:50]}...'")
            return translation
            
        except Exception as e:
            logger.error(f"Translation error: {e}")
            raise ModelInferenceError(f"Translation failed: {e}")
    
    def handle_client(self, client_socket):
        """クライアント接続処理"""
        try:
            # データ受信
            data = client_socket.recv(4096).decode('utf-8')
            request_data = json.loads(data.strip())
            
            # リクエスト処理
            text = request_data.get('text', '')
            source_lang = request_data.get('source_lang', 'en') 
            target_lang = request_data.get('target_lang', 'ja')
            
            logger.info(f"Processing: '{text[:50]}...' [{source_lang}->{target_lang}]")
            
            # 非同期翻訳実行
            loop = asyncio.new_event_loop()
            asyncio.set_event_loop(loop)
            
            start_time = time.time()
            try:
                translation = loop.run_until_complete(
                    self.translate_text(text, source_lang, target_lang)
                )
                processing_time = time.time() - start_time
                
                # レスポンス作成
                response = {
                    "success": True,
                    "translation": translation,
                    "processing_time": processing_time,
                    "source_lang": source_lang,
                    "target_lang": target_lang
                }
                
            except Exception as e:
                processing_time = time.time() - start_time
                logger.error(f"Translation failed: {e}")
                response = {
                    "success": False,
                    "error": str(e),
                    "processing_time": processing_time,
                    "source_lang": source_lang,
                    "target_lang": target_lang
                }
            finally:
                loop.close()
            
            # レスポンス送信
            response_json = json.dumps(response, ensure_ascii=False) + '\n'
            client_socket.sendall(response_json.encode('utf-8'))
            
        except Exception as e:
            logger.error(f"Client handling error: {e}")
            error_response = {
                "success": False,
                "error": f"Server error: {e}"
            }
            try:
                response_json = json.dumps(error_response, ensure_ascii=False) + '\n'
                client_socket.sendall(response_json.encode('utf-8'))
            except:
                pass
        finally:
            client_socket.close()
    
    async def start_server(self):
        """サーバー起動"""
        try:
            # モデルロード
            await self.load_model()
            
            # TCP サーバー起動
            self.server_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            self.server_socket.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
            self.server_socket.bind(('127.0.0.1', self.port))
            self.server_socket.listen(5)
            
            self.running = True
            logger.info(f"NLLB-200 Translation Server listening on 127.0.0.1:{self.port}")
            
            # クライアント接続待機
            while self.running:
                try:
                    client_socket, address = self.server_socket.accept()
                    logger.debug(f"Client connected: {address}")
                    
                    # スレッドプールで処理
                    self.executor.submit(self.handle_client, client_socket)
                    
                except Exception as e:
                    if self.running:
                        logger.error(f"Accept error: {e}")
                        
        except Exception as e:
            logger.error(f"Server start error: {e}")
            raise
    
    def stop_server(self):
        """サーバー停止"""
        logger.info("Stopping translation server...")
        self.running = False
        if self.server_socket:
            self.server_socket.close()
        self.executor.shutdown(wait=True)

# メイン実行
async def main():
    parser = argparse.ArgumentParser(description='Dynamic Port NLLB-200 Translation Server')
    parser.add_argument('--port', type=int, default=5000, help='Server port (default: 5000)')
    args = parser.parse_args()
    
    server = DynamicNLLBTranslationServer(port=args.port)
    
    # シグナルハンドラー設定
    def signal_handler(sig, frame):
        logger.info("Received shutdown signal")
        server.stop_server()
        sys.exit(0)
    
    signal.signal(signal.SIGINT, signal_handler)
    signal.signal(signal.SIGTERM, signal_handler)
    
    try:
        await server.start_server()
    except KeyboardInterrupt:
        logger.info("Server shutdown requested")
    finally:
        server.stop_server()

if __name__ == "__main__":
    asyncio.run(main())