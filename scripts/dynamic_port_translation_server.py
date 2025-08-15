#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
動的ポート対応翻訳サーバー
Issue #147 Phase 5: ポート競合防止機構

既存のoptimized_translation_server.pyをベースに動的ポート対応を追加
"""

import argparse
import asyncio
import json
import logging
import socket
import sys
import time
import traceback
from typing import Dict, Optional, Any, List

import torch
from transformers import AutoTokenizer, AutoModelForSeq2SeqLM, MarianMTModel, MarianTokenizer

# ログ設定
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(name)s - %(levelname)s - %(message)s',
    handlers=[
        logging.StreamHandler(sys.stdout),
        logging.FileHandler('translation_server.log', encoding='utf-8')
    ]
)
logger = logging.getLogger(__name__)

class DynamicPortTranslationServer:
    """動的ポート対応翻訳サーバークラス"""
    
    def __init__(self, port: int = 5555, language_pair: str = "ja-en"):
        self.port = port
        self.language_pair = language_pair
        self.device = "cuda" if torch.cuda.is_available() else "cpu"
        self.models: Dict[str, tuple] = {}
        self.server_socket: Optional[socket.socket] = None
        self.is_running = False
        
        logger.info(f"🚀 DynamicPortTranslationServer初期化")
        logger.info(f"   Port: {port}")
        logger.info(f"   Language Pair: {language_pair}")
        logger.info(f"   Device: {self.device}")
        
    async def initialize_models(self):
        """翻訳モデルの初期化"""
        try:
            logger.info("🔄 翻訳モデル初期化開始")
            
            # 指定された言語ペアに応じてモデルを読み込み
            if self.language_pair == "ja-en":
                await self._load_ja_en_model()
            elif self.language_pair == "en-ja":
                await self._load_en_ja_model()
            else:
                logger.warning(f"⚠️ 未対応の言語ペア: {self.language_pair}. ja-enモデルを読み込みます")
                await self._load_ja_en_model()
                
            logger.info("✅ 翻訳モデル初期化完了")
            
        except Exception as e:
            logger.error(f"❌ モデル初期化エラー: {e}")
            raise
    
    async def _load_ja_en_model(self):
        """日本語→英語モデル読み込み"""
        try:
            model_name = "Helsinki-NLP/opus-mt-ja-en"
            logger.info(f"🔄 日本語→英語モデルロード開始: {model_name}")
            
            tokenizer = MarianTokenizer.from_pretrained(model_name)
            model = MarianMTModel.from_pretrained(model_name).to(self.device)
            model.eval()
            
            self.models["ja-en"] = (model, tokenizer)
            logger.info("✅ 日本語→英語モデルロード完了")
            
        except Exception as e:
            logger.error(f"❌ 日本語→英語モデルロード失敗: {e}")
            raise
    
    async def _load_en_ja_model(self):
        """英語→日本語モデル読み込み（NLLB-200使用）"""
        try:
            # Phase 4で実装されたNLLB-200モデルを使用
            model_name = "facebook/nllb-200-distilled-600M"
            logger.info(f"🔄 英語→日本語モデル（NLLB-200）ロード開始: {model_name}")
            
            tokenizer = AutoTokenizer.from_pretrained(model_name)
            model = AutoModelForSeq2SeqLM.from_pretrained(model_name).to(self.device)
            model.eval()
            
            self.models["en-ja"] = (model, tokenizer)
            logger.info("✅ 英語→日本語モデル（NLLB-200）ロード完了")
            
        except Exception as e:
            logger.error(f"❌ 英語→日本語モデル（NLLB-200）ロード失敗: {e}")
            # フォールバック: Helsinki-NLPモデル（汚染問題あり）
            try:
                model_name_fallback = "Helsinki-NLP/opus-mt-en-jap"
                logger.warning(f"⚠️ フォールバック: {model_name_fallback}を試行（汚染リスクあり）")
                
                tokenizer = MarianTokenizer.from_pretrained(model_name_fallback)
                model = MarianMTModel.from_pretrained(model_name_fallback).to(self.device)
                model.eval()
                
                self.models["en-ja"] = (model, tokenizer)
                logger.warning("⚠️ 英語→日本語モデル（Helsinki-NLP）フォールバック完了")
                
            except Exception as fallback_error:
                logger.error(f"❌ フォールバックモデルロードも失敗: {fallback_error}")
                raise
    
    async def start_server(self):
        """サーバー開始"""
        try:
            # モデル初期化
            await self.initialize_models()
            
            # ソケット作成・バインド
            self.server_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            self.server_socket.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
            self.server_socket.bind(('127.0.0.1', self.port))
            self.server_socket.listen(5)
            self.server_socket.setblocking(False)
            
            self.is_running = True
            
            logger.info(f"🌟 翻訳サーバー起動完了")
            logger.info(f"   Address: 127.0.0.1:{self.port}")
            logger.info(f"   Language Pair: {self.language_pair}")
            logger.info(f"   Models Loaded: {list(self.models.keys())}")
            
            # クライアント接続待機
            while self.is_running:
                try:
                    # 非同期でクライアント接続を待機
                    client_socket, client_addr = await asyncio.get_event_loop().sock_accept(self.server_socket)
                    
                    # クライアント処理を非同期で実行
                    asyncio.create_task(self._handle_client(client_socket, client_addr))
                    
                except asyncio.CancelledError:
                    logger.info("📴 サーバー停止要求を受信")
                    break
                except Exception as e:
                    logger.error(f"❌ クライアント接続エラー: {e}")
                    await asyncio.sleep(0.1)
                    
        except Exception as e:
            logger.error(f"❌ サーバー起動エラー: {e}")
            raise
        finally:
            await self._cleanup()
    
    async def _handle_client(self, client_socket: socket.socket, client_addr):
        """クライアント処理"""
        try:
            logger.debug(f"🔗 クライアント接続: {client_addr}")
            
            # リクエスト受信
            client_socket.settimeout(30.0)  # 30秒タイムアウト
            
            # データ受信（改行区切り）
            data = b""
            while b'\n' not in data:
                chunk = client_socket.recv(4096)
                if not chunk:
                    break
                data += chunk
            
            if not data:
                logger.warning(f"⚠️ 空のリクエスト: {client_addr}")
                return
            
            # JSON解析
            request_text = data.decode('utf-8').strip()
            request = json.loads(request_text)
            
            # 翻訳処理
            response = await self._process_translation_request(request)
            
            # レスポンス送信
            response_json = json.dumps(response, ensure_ascii=False)
            client_socket.sendall(response_json.encode('utf-8'))
            
            logger.debug(f"✅ クライアント処理完了: {client_addr}")
            
        except json.JSONDecodeError as e:
            logger.error(f"❌ JSON解析エラー: {e}")
            error_response = {"success": False, "error": "Invalid JSON format"}
            client_socket.sendall(json.dumps(error_response).encode('utf-8'))
            
        except Exception as e:
            logger.error(f"❌ クライアント処理エラー: {e}")
            error_response = {"success": False, "error": str(e)}
            try:
                client_socket.sendall(json.dumps(error_response).encode('utf-8'))
            except:
                pass
        finally:
            try:
                client_socket.close()
            except:
                pass
    
    async def _process_translation_request(self, request: Dict[str, Any]) -> Dict[str, Any]:
        """翻訳リクエスト処理"""
        start_time = time.time()
        
        try:
            text = request.get("text", "")
            source_lang = request.get("source_lang", "ja")
            target_lang = request.get("target_lang", "en")
            
            if not text.strip():
                return {
                    "success": False,
                    "error": "Empty text provided",
                    "processing_time": time.time() - start_time
                }
            
            # 言語ペア決定
            lang_pair = f"{source_lang}-{target_lang}"
            
            # モデル選択
            if lang_pair in self.models:
                model, tokenizer = self.models[lang_pair]
            elif lang_pair == "en-ja" and "en-ja" in self.models:
                model, tokenizer = self.models["en-ja"]
            elif lang_pair == "ja-en" and "ja-en" in self.models:
                model, tokenizer = self.models["ja-en"]
            else:
                # フォールバック: 利用可能な最初のモデル
                if self.models:
                    model, tokenizer = next(iter(self.models.values()))
                    logger.warning(f"⚠️ 言語ペア {lang_pair} 未対応。フォールバックモデル使用")
                else:
                    return {
                        "success": False,
                        "error": f"No model available for language pair: {lang_pair}",
                        "processing_time": time.time() - start_time
                    }
            
            # 翻訳実行
            translation = await self._translate_text(text, model, tokenizer, source_lang, target_lang)
            
            processing_time = time.time() - start_time
            
            return {
                "success": True,
                "translation": translation,
                "processing_time": processing_time,
                "language_pair": lang_pair,
                "model_info": {
                    "device": self.device,
                    "server_port": self.port
                }
            }
            
        except Exception as e:
            logger.error(f"❌ 翻訳処理エラー: {e}")
            return {
                "success": False,
                "error": str(e),
                "processing_time": time.time() - start_time
            }
    
    async def _translate_text(self, text: str, model, tokenizer, source_lang: str, target_lang: str) -> str:
        """テキスト翻訳実行"""
        try:
            # NLLB-200モデルの場合の言語コード変換
            if "nllb" in str(type(model)).lower():
                # BCP-47言語コードへの変換
                lang_code_map = {
                    "en": "eng_Latn",
                    "ja": "jpn_Jpan"
                }
                src_code = lang_code_map.get(source_lang, source_lang)
                
                # NLLB-200用のトークン化
                inputs = tokenizer(text, return_tensors="pt", padding=True, truncation=True, max_length=512)
                inputs = {k: v.to(self.device) for k, v in inputs.items()}
                
                # 強制言語ID設定
                forced_bos_token_id = tokenizer.lang_code_to_id.get(lang_code_map.get(target_lang, target_lang))
                
                with torch.no_grad():
                    outputs = model.generate(
                        **inputs,
                        forced_bos_token_id=forced_bos_token_id,
                        max_length=512,
                        num_beams=4,
                        early_stopping=True
                    )
                
                translation = tokenizer.decode(outputs[0], skip_special_tokens=True)
                
            else:
                # Helsinki-NLP OPUS-MTモデルの場合
                inputs = tokenizer(text, return_tensors="pt", padding=True, truncation=True, max_length=512)
                inputs = {k: v.to(self.device) for k, v in inputs.items()}
                
                with torch.no_grad():
                    outputs = model.generate(
                        **inputs,
                        max_length=512,
                        num_beams=4,
                        early_stopping=True
                    )
                
                translation = tokenizer.decode(outputs[0], skip_special_tokens=True)
            
            return translation.strip()
            
        except Exception as e:
            logger.error(f"❌ 翻訳実行エラー: {e}")
            raise
    
    async def _cleanup(self):
        """リソースクリーンアップ"""
        logger.info("🧹 サーバークリーンアップ開始")
        
        self.is_running = False
        
        if self.server_socket:
            try:
                self.server_socket.close()
            except Exception as e:
                logger.warning(f"⚠️ サーバーソケットクローズエラー: {e}")
        
        # モデルのメモリ解放
        for lang_pair, (model, tokenizer) in self.models.items():
            try:
                del model
                del tokenizer
                logger.debug(f"🧹 {lang_pair}モデル解放完了")
            except Exception as e:
                logger.warning(f"⚠️ {lang_pair}モデル解放エラー: {e}")
        
        self.models.clear()
        
        # GPU メモリクリア
        if torch.cuda.is_available():
            torch.cuda.empty_cache()
            logger.debug("🧹 GPUメモリクリア完了")
        
        logger.info("✅ サーバークリーンアップ完了")

def parse_arguments():
    """コマンドライン引数解析"""
    parser = argparse.ArgumentParser(description="Dynamic Port Translation Server")
    parser.add_argument("--port", type=int, default=5555, 
                       help="Server port (default: 5555)")
    parser.add_argument("--language-pair", type=str, default="ja-en",
                       choices=["ja-en", "en-ja"],
                       help="Language pair (default: ja-en)")
    parser.add_argument("--log-level", type=str, default="INFO",
                       choices=["DEBUG", "INFO", "WARNING", "ERROR"],
                       help="Log level (default: INFO)")
    
    return parser.parse_args()

async def main():
    """メイン関数"""
    try:
        args = parse_arguments()
        
        # ログレベル設定
        logging.getLogger().setLevel(getattr(logging, args.log_level))
        
        logger.info("🚀 Dynamic Port Translation Server 起動")
        logger.info(f"   Port: {args.port}")
        logger.info(f"   Language Pair: {args.language_pair}")
        logger.info(f"   Log Level: {args.log_level}")
        
        # サーバー作成・起動
        server = DynamicPortTranslationServer(
            port=args.port,
            language_pair=args.language_pair
        )
        
        await server.start_server()
        
    except KeyboardInterrupt:
        logger.info("📴 Ctrl+C による停止要求")
    except Exception as e:
        logger.error(f"❌ サーバーエラー: {e}")
        logger.error(f"❌ スタックトレース:\n{traceback.format_exc()}")
        sys.exit(1)
    finally:
        logger.info("👋 Dynamic Port Translation Server 終了")

if __name__ == "__main__":
    asyncio.run(main())