#!/usr/bin/env python3
"""
完全にクリーンな翻訳サーバー - マグブキ汚染問題解決用
モデル状態の完全分離と厳密な初期化を実装
"""
import asyncio
import json
import logging
import time
import gc
import sys
import os
import argparse
from typing import Dict, Optional, Any
import torch
from transformers import MarianMTModel, MarianTokenizer

# ログ設定
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(name)s - %(levelname)s - %(message)s'
)
logger = logging.getLogger(__name__)

class CleanTranslationServer:
    """完全にクリーンな翻訳サーバー - 汚染防止機構"""
    
    def __init__(self, port: int = 5555):
        self.port = port
        self.models = {}
        self.tokenizers = {}
        self.request_count = 0
        
    def _force_model_reset(self, model):
        """モデルの強制リセット - 汚染除去"""
        try:
            # 1. 評価モードに強制設定
            model.eval()
            
            # 2. 勾配計算完全無効化
            for param in model.parameters():
                param.grad = None
                param.requires_grad = False
                
            # 3. PyTorchメモリ完全クリア
            if torch.cuda.is_available():
                torch.cuda.empty_cache()
                torch.cuda.synchronize()
                
            # 4. ガベージコレクション強制実行
            gc.collect()
            
            logger.info("🧹 モデル状態完全リセット完了")
        except Exception as e:
            logger.error(f"モデルリセットエラー: {e}")
    
    async def _initialize_models(self):
        """モデルの厳密な初期化"""
        try:
            logger.info("🚀 CLEAN_MODEL_INIT: クリーンモデル初期化開始")
            
            # 日本語→英語モデル
            model_name = "E:/dev/Baketa/Models/opus-mt-ja-en"
            
            logger.info("📦 モデルロード開始 - 厳密な初期化")
            self.models['ja-en'] = MarianMTModel.from_pretrained(model_name)
            self.tokenizers['ja-en'] = MarianTokenizer.from_pretrained(model_name)
            
            # 強制リセット実行
            self._force_model_reset(self.models['ja-en'])
            
            logger.info("✅ 日本語→英語モデル初期化完了")
            
            # 英語→日本語モデル（完全なモデルファイルが必要）
            en_ja_model = "E:/dev/Baketa/Models/opus-mt-en-ja" 
            config_file = os.path.join(en_ja_model, "config.json")
            tokenizer_config = os.path.join(en_ja_model, "tokenizer_config.json")
            
            if os.path.exists(config_file) and os.path.exists(tokenizer_config):
                try:
                    self.models['en-ja'] = MarianMTModel.from_pretrained(en_ja_model)
                    self.tokenizers['en-ja'] = MarianTokenizer.from_pretrained(en_ja_model)
                    self._force_model_reset(self.models['en-ja'])
                    logger.info("✅ 英語→日本語モデル初期化完了")
                except Exception as e:
                    logger.warning(f"⚠️ 英語→日本語モデル初期化失敗: {e}")
            else:
                logger.warning("⚠️ 英語→日本語モデルが不完全です（config.json または tokenizer_config.json が不足）")
            
            logger.info("🎯 CLEAN_MODEL_READY: すべてのモデル初期化完了")
            
        except Exception as e:
            logger.error(f"モデル初期化エラー: {e}")
            raise
    
    def _clean_translate(self, text: str, model_key: str) -> str:
        """完全にクリーンな翻訳実行"""
        try:
            # モデルの存在確認
            if model_key not in self.models or model_key not in self.tokenizers:
                logger.error(f"モデルが利用できません: {model_key}")
                return f"Translation Error: Model not available for '{model_key}'"
                
            model = self.models[model_key]
            tokenizer = self.tokenizers[model_key]
            
            # 翻訳前の強制クリーンアップ
            self._force_model_reset(model)
            
            # トークン化（厳密）
            inputs = tokenizer(text, return_tensors="pt", padding=True, truncation=True, max_length=512)
            
            # 翻訳生成（決定的設定）
            with torch.no_grad():
                outputs = model.generate(
                    **inputs,
                    max_length=512,
                    num_beams=1,  # ビーム探索無効化
                    do_sample=False,  # サンプリング無効化
                    temperature=1.0,
                    pad_token_id=tokenizer.pad_token_id,
                    eos_token_id=tokenizer.eos_token_id,
                    early_stopping=False  # 早期停止無効化
                )
            
            # デコード（厳密）
            translation = tokenizer.decode(outputs[0], skip_special_tokens=True)
            
            # 翻訳後のクリーンアップ
            del inputs, outputs
            self._force_model_reset(model)
            
            logger.info(f"✅ CLEAN_TRANSLATION: '{text}' -> '{translation}'")
            return translation.strip()
            
        except Exception as e:
            logger.error(f"翻訳エラー: {e}")
            return f"Translation Error: {str(e)}"
    
    async def _handle_translation_request(self, request_data: dict) -> dict:
        """翻訳リクエスト処理"""
        try:
            text = request_data.get('text', '')
            source_lang = request_data.get('source_lang', 'ja')
            target_lang = request_data.get('target_lang', 'en')
            
            # モデルキー決定
            if source_lang == 'ja' and target_lang == 'en':
                model_key = 'ja-en'
            elif source_lang == 'en' and target_lang == 'ja':
                model_key = 'en-ja'
            else:
                return {
                    'success': False,
                    'error': f'Unsupported language pair: {source_lang}->{target_lang}',
                    'translation': '',
                    'confidence': 0.0
                }
            
            # クリーン翻訳実行
            start_time = time.time()
            translation = self._clean_translate(text, model_key)
            processing_time = time.time() - start_time
            
            self.request_count += 1
            
            # 翻訳エラーチェック - "Translation Error:"で始まる場合は失敗として扱う
            if translation.startswith("Translation Error:"):
                return {
                    'success': False,
                    'error': translation,
                    'translation': '',
                    'confidence': 0.0,
                    'processing_time': processing_time
                }
            
            return {
                'success': True,
                'translation': translation,
                'confidence': 0.95,
                'error': None,
                'processing_time': processing_time
            }
            
        except Exception as e:
            logger.error(f"リクエスト処理エラー: {e}")
            return {
                'success': False,
                'error': str(e),
                'translation': '',
                'confidence': 0.0
            }
    
    async def _handle_client(self, reader: asyncio.StreamReader, writer: asyncio.StreamWriter):
        """クライアント接続処理"""
        client_address = writer.get_extra_info('peername')
        logger.info(f"🔗 Client connected: {client_address}")
        
        try:
            while True:
                data = await reader.readline()
                if not data:
                    break
                    
                try:
                    request_str = data.decode('utf-8').strip()
                    request_data = json.loads(request_str)
                    
                    # Ping処理
                    if 'ping' in request_data:
                        response = {
                            'success': True,
                            'pong': True,
                            'status': 'clean_server_ready',
                            'processing_time': 0.001
                        }
                    else:
                        # 翻訳処理
                        response = await self._handle_translation_request(request_data)
                    
                    # レスポンス送信
                    response_str = json.dumps(response, ensure_ascii=False) + '\n'
                    writer.write(response_str.encode('utf-8'))
                    await writer.drain()
                    
                except json.JSONDecodeError as e:
                    logger.error(f"JSON decode error: {e}")
                    error_response = {
                        'success': False,
                        'error': f'Invalid JSON: {str(e)}',
                        'translation': '',
                        'confidence': 0.0
                    }
                    response_str = json.dumps(error_response) + '\n'
                    writer.write(response_str.encode('utf-8'))
                    await writer.drain()
                    
        except Exception as e:
            logger.error(f"Client handler error: {e}")
        finally:
            try:
                writer.close()
                await writer.wait_closed()
            except Exception as e:
                logger.error(f"Connection close error: {e}")
            logger.info(f"🔌 Client disconnected: {client_address}")
    
    async def start_server(self):
        """サーバー起動"""
        try:
            # モデル初期化
            await self._initialize_models()
            
            # サーバー起動
            server = await asyncio.start_server(
                self._handle_client,
                '127.0.0.1',
                self.port
            )
            
            logger.info(f"🚀 Clean Translation Server listening on 127.0.0.1:{self.port}")
            logger.info("🎯 Ready for clean translations - マグブキ汚染問題解決版")
            
            async with server:
                await server.serve_forever()
                
        except Exception as e:
            logger.error(f"Server error: {e}")
            raise

async def main():
    parser = argparse.ArgumentParser(description='Clean Translation Server')
    parser.add_argument('--port', type=int, default=5555, help='Server port')
    args = parser.parse_args()
    
    server = CleanTranslationServer(args.port)
    await server.start_server()

if __name__ == "__main__":
    asyncio.run(main())