#!/usr/bin/env python3
"""
Baketa OPUS-MT Persistent Translation Server
常駐型高速翻訳サーバー - モデル初回ロード後は0.1-0.5秒で翻訳実行
"""

import sys
import json
import os
import warnings
import socket
import threading
import time
from datetime import datetime
import traceback

# 警告を抑制してパフォーマンス向上
warnings.filterwarnings("ignore", category=UserWarning)
os.environ['TOKENIZERS_PARALLELISM'] = 'false'  # tokenizerの並列処理を無効化

from transformers import AutoTokenizer, AutoModelForSeq2SeqLM, logging
import torch

# transformersのログレベルを下げる
logging.set_verbosity_error()

# 🔄 動的モデル選択: 言語方向に応じてモデルIDを決定
MODELS = {
    "ja-en": "Helsinki-NLP/opus-mt-ja-en",  # 日→英
    "en-ja": "Helsinki-NLP/opus-mt-en-jap"  # 英→日（正式名称: "jap"）
}
SERVER_HOST = "127.0.0.1"
SERVER_PORT = 29876  # Baketa専用ポート

class PersistentOpusMtServer:
    """
    OPUS-MT常駐翻訳サーバー
    - 1回のモデルロードで高速翻訳を継続提供
    - TCP Socket通信でC#と連携
    - プロセス死活監視対応
    """
    
    def __init__(self):
        # 🔄 複数モデル同時サポート: 日→英、英→日の両方向
        self.tokenizers = {}  # {"ja-en": tokenizer, "en-ja": tokenizer}
        self.models = {}      # {"ja-en": model, "en-ja": model}
        self.initialized = False
        self.server_socket = None
        self.running = False
        self.translation_count = 0
        self.start_time = datetime.now()
        
    def initialize_models(self):
        """
        🔄 複数モデル同時初期化: 日→英、英→日の両方向サポート
        """
        try:
            for direction, model_id in MODELS.items():
                print(f"🔄 [{datetime.now().strftime('%H:%M:%S')}] Loading tokenizer for {direction} ({model_id})...", 
                      file=sys.stderr, flush=True)
                self.tokenizers[direction] = AutoTokenizer.from_pretrained(model_id, local_files_only=False)
                
                print(f"🔄 [{datetime.now().strftime('%H:%M:%S')}] Loading model for {direction} ({model_id})...", 
                      file=sys.stderr, flush=True)
                self.models[direction] = AutoModelForSeq2SeqLM.from_pretrained(model_id, local_files_only=False)
                
                print(f"✅ [{datetime.now().strftime('%H:%M:%S')}] Model {direction} loaded successfully!", 
                      file=sys.stderr, flush=True)
            
            print(f"✅ [{datetime.now().strftime('%H:%M:%S')}] All models initialization complete! Ready for bidirectional translation.", 
                  file=sys.stderr, flush=True)
            self.initialized = True
            return True
        except Exception as e:
            print(f"❌ [{datetime.now().strftime('%H:%M:%S')}] Models initialization failed: {e}", 
                  file=sys.stderr, flush=True)
            print(f"📄 Traceback:\n{traceback.format_exc()}", file=sys.stderr, flush=True)
            return False
    
    def translate(self, text, direction="ja-en"):
        """
        🔄 双方向翻訳（高速実行：0.1-0.5秒目標）
        Args:
            text: 翻訳するテキスト
            direction: 翻訳方向 ("ja-en" or "en-ja")
        """
        if not self.initialized:
            return {"success": False, "error": "Service not initialized"}
        
        if direction not in self.models:
            return {"success": False, "error": f"Unsupported direction: {direction}"}
        
        start_time = time.time()
        
        try:
            # 指定された言語方向のモデルを使用
            tokenizer = self.tokenizers[direction]
            model = self.models[direction]
            
            # HuggingFace Transformers標準処理
            inputs = tokenizer(text, return_tensors="pt")
            
            with torch.no_grad():
                outputs = model.generate(
                    **inputs,
                    max_length=128,
                    num_beams=3,
                    length_penalty=1.0,
                    early_stopping=True
                )
            
            translation = tokenizer.decode(outputs[0], skip_special_tokens=True)
            
            processing_time = time.time() - start_time
            self.translation_count += 1
            
            print(f"⚡ [{datetime.now().strftime('%H:%M:%S')}] Translation #{self.translation_count} [{direction}] completed in {processing_time:.3f}s: '{text}' → '{translation}'", 
                  file=sys.stderr, flush=True)
            
            return {
                "success": True,
                "translation": translation,
                "source": text,
                "direction": direction,
                "processing_time": processing_time,
                "translation_count": self.translation_count
            }
            
        except Exception as e:
            processing_time = time.time() - start_time
            print(f"❌ [{datetime.now().strftime('%H:%M:%S')}] Translation failed after {processing_time:.3f}s: {e}", 
                  file=sys.stderr, flush=True)
            return {
                "success": False,
                "error": str(e),
                "source": text,
                "processing_time": processing_time
            }

    def translate_batch(self, texts, direction="ja-en"):
        """
        🔄 双方向バッチ翻訳：複数テキストを一度に処理（効率化）
        Args:
            texts: 翻訳するテキストのリスト
            direction: 翻訳方向 ("ja-en" or "en-ja")
        """
        if not self.initialized:
            return {"success": False, "error": "Service not initialized"}
        
        if direction not in self.models:
            return {"success": False, "error": f"Unsupported direction: {direction}"}
        
        start_time = time.time()
        translations = []
        sources = []
        
        try:
            print(f"📦 [{datetime.now().strftime('%H:%M:%S')}] Batch translation [{direction}] started - {len(texts)} texts", 
                  file=sys.stderr, flush=True)
            
            # 指定された言語方向のモデルを使用
            tokenizer = self.tokenizers[direction]
            model = self.models[direction]
            
            # バッチ処理：複数テキストを一度にエンコード
            inputs = tokenizer(texts, return_tensors="pt", padding=True, truncation=True)
            
            with torch.no_grad():
                outputs = model.generate(
                    **inputs,
                    max_length=128,
                    num_beams=3,
                    length_penalty=1.0,
                    early_stopping=True
                )
            
            # 各出力をデコード
            for i, output in enumerate(outputs):
                translation = tokenizer.decode(output, skip_special_tokens=True)
                translations.append(translation)
                sources.append(texts[i] if i < len(texts) else "")
                
                self.translation_count += 1
                
                print(f"⚡ [{datetime.now().strftime('%H:%M:%S')}] Batch item #{i+1} [{direction}]: '{texts[i] if i < len(texts) else ''}' → '{translation}'", 
                      file=sys.stderr, flush=True)
            
            processing_time = time.time() - start_time
            
            print(f"✅ [{datetime.now().strftime('%H:%M:%S')}] Batch translation completed in {processing_time:.3f}s - {len(translations)} translations", 
                  file=sys.stderr, flush=True)
            
            return {
                "success": True,
                "translations": translations,
                "sources": sources,
                "direction": direction,
                "processing_time": processing_time,
                "translation_count": len(translations)
            }
            
        except Exception as e:
            processing_time = time.time() - start_time
            print(f"❌ [{datetime.now().strftime('%H:%M:%S')}] Batch translation failed after {processing_time:.3f}s: {e}", 
                  file=sys.stderr, flush=True)
            return {
                "success": False,
                "error": str(e),
                "sources": texts,
                "processing_time": processing_time
            }
    
    def handle_client(self, client_socket, client_address):
        """
        クライアント接続処理
        """
        print(f"🔗 [{datetime.now().strftime('%H:%M:%S')}] Client connected from {client_address}", 
              file=sys.stderr, flush=True)
        
        try:
            while self.running:
                # リクエスト受信（改行区切りJSON）
                data = client_socket.recv(4096).decode('utf-8').strip()
                if not data:
                    break
                
                # 特殊コマンド処理
                if data == "PING":
                    response = {"status": "alive", "uptime_seconds": (datetime.now() - self.start_time).total_seconds(), 
                               "translation_count": self.translation_count}
                    client_socket.send((json.dumps(response, ensure_ascii=False) + "\n").encode('utf-8'))
                    continue
                elif data == "SHUTDOWN":
                    self.running = False
                    response = {"status": "shutting_down"}
                    client_socket.send((json.dumps(response, ensure_ascii=False) + "\n").encode('utf-8'))
                    break
                
                # 翻訳リクエスト処理
                try:
                    request = json.loads(data)
                    
                    # バッチ翻訳リクエストかチェック
                    if "batch_texts" in request:
                        texts = request.get("batch_texts", [])
                        direction = request.get("direction", "ja-en")  # 🔄 言語方向情報取得
                        
                        if not texts or not any(text.strip() for text in texts):
                            response = {"success": False, "error": "Empty batch texts"}
                        else:
                            response = self.translate_batch(texts, direction)
                    
                    # 単一翻訳リクエスト
                    else:
                        text = request.get("text", "")
                        direction = request.get("direction", "ja-en")  # 🔄 言語方向情報取得
                        
                        if not text:
                            response = {"success": False, "error": "Empty text"}
                        else:
                            # 改行文字を含む場合はバッチ処理で対応
                            if "\n" in text:
                                # 改行で分割し、空行を除去
                                text_lines = [line.strip() for line in text.split("\n") if line.strip()]
                                
                                if not text_lines:
                                    response = {"success": False, "error": "Empty text after splitting"}
                                elif len(text_lines) == 1:
                                    # 実際は1行だった場合は通常翻訳
                                    response = self.translate(text_lines[0], direction)
                                else:
                                    # 複数行の場合はバッチ翻訳して結合
                                    print(f"📄 [{datetime.now().strftime('%H:%M:%S')}] Multi-line text detected - splitting into {len(text_lines)} lines", 
                                          file=sys.stderr, flush=True)
                                    batch_result = self.translate_batch(text_lines, direction)
                                    
                                    if batch_result["success"]:
                                        # バッチ結果を改行で結合
                                        combined_translation = "\n".join(batch_result["translations"])
                                        response = {
                                            "success": True,
                                            "translation": combined_translation,
                                            "source": text,
                                            "processing_time": batch_result["processing_time"],
                                            "translation_count": batch_result["translation_count"],
                                            "split_lines": len(text_lines)  # デバッグ用
                                        }
                                    else:
                                        response = batch_result
                            else:
                                # 改行なしの通常翻訳
                                response = self.translate(text, direction)
                    
                    # レスポンス送信
                    response_json = json.dumps(response, ensure_ascii=False) + "\n"
                    client_socket.send(response_json.encode('utf-8'))
                    
                except json.JSONDecodeError as e:
                    error_response = {"success": False, "error": f"Invalid JSON: {str(e)}"}
                    client_socket.send((json.dumps(error_response, ensure_ascii=False) + "\n").encode('utf-8'))
                
        except Exception as e:
            print(f"❌ [{datetime.now().strftime('%H:%M:%S')}] Client handling error: {e}", 
                  file=sys.stderr, flush=True)
        finally:
            client_socket.close()
            print(f"🔗 [{datetime.now().strftime('%H:%M:%S')}] Client {client_address} disconnected", 
                  file=sys.stderr, flush=True)
    
    def start_server(self):
        """
        TCP socketサーバー開始
        """
        try:
            self.server_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            self.server_socket.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
            self.server_socket.bind((SERVER_HOST, SERVER_PORT))
            self.server_socket.listen(5)
            self.running = True
            
            print(f"🚀 [{datetime.now().strftime('%H:%M:%S')}] OPUS-MT Persistent Server started on {SERVER_HOST}:{SERVER_PORT}", 
                  file=sys.stderr, flush=True)
            print(f"📊 [{datetime.now().strftime('%H:%M:%S')}] Server status: Model loaded, ready for high-speed translation", 
                  file=sys.stderr, flush=True)
            
            while self.running:
                try:
                    client_socket, client_address = self.server_socket.accept()
                    client_thread = threading.Thread(
                        target=self.handle_client, 
                        args=(client_socket, client_address),
                        daemon=True
                    )
                    client_thread.start()
                except Exception as e:
                    if self.running:  # サーバーが実行中の場合のみエラー表示
                        print(f"❌ [{datetime.now().strftime('%H:%M:%S')}] Accept error: {e}", 
                              file=sys.stderr, flush=True)
                    
        except Exception as e:
            print(f"❌ [{datetime.now().strftime('%H:%M:%S')}] Server startup failed: {e}", 
                  file=sys.stderr, flush=True)
            return False
        finally:
            self.cleanup()
        
        return True
    
    def cleanup(self):
        """
        サーバー終了処理
        """
        self.running = False
        if self.server_socket:
            self.server_socket.close()
        print(f"🛑 [{datetime.now().strftime('%H:%M:%S')}] Server stopped. Total translations: {self.translation_count}", 
              file=sys.stderr, flush=True)

def main():
    """
    常駐サーバーメインエントリーポイント
    """
    # Windows環境でUTF-8出力を強制
    import codecs
    sys.stdout = codecs.getwriter('utf-8')(sys.stdout.buffer)
    sys.stderr = codecs.getwriter('utf-8')(sys.stderr.buffer)
    
    print(f"🎯 [{datetime.now().strftime('%H:%M:%S')}] Baketa OPUS-MT Persistent Server starting...", 
          file=sys.stderr, flush=True)
    
    server = PersistentOpusMtServer()
    
    # モデル初期化
    if not server.initialize_models():
        print(f"💥 [{datetime.now().strftime('%H:%M:%S')}] Failed to initialize model. Exiting.", 
              file=sys.stderr, flush=True)
        sys.exit(1)
    
    # サーバー開始
    try:
        server.start_server()
    except KeyboardInterrupt:
        print(f"\n🛑 [{datetime.now().strftime('%H:%M:%S')}] Server interrupted by user", 
              file=sys.stderr, flush=True)
    except Exception as e:
        print(f"💥 [{datetime.now().strftime('%H:%M:%S')}] Server crashed: {e}", 
              file=sys.stderr, flush=True)
        print(f"📄 Traceback:\n{traceback.format_exc()}", file=sys.stderr, flush=True)
        sys.exit(1)

if __name__ == "__main__":
    main()