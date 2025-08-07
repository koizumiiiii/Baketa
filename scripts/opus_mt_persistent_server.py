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

MODEL_ID = "Helsinki-NLP/opus-mt-ja-en"
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
        self.tokenizer = None
        self.model = None
        self.initialized = False
        self.server_socket = None
        self.running = False
        self.translation_count = 0
        self.start_time = datetime.now()
        
    def initialize_model(self):
        """
        HuggingFace Transformersでモデル初期化（1回のみ実行）
        """
        try:
            print(f"🔄 [{datetime.now().strftime('%H:%M:%S')}] Loading tokenizer for {MODEL_ID}...", 
                  file=sys.stderr, flush=True)
            self.tokenizer = AutoTokenizer.from_pretrained(MODEL_ID, local_files_only=False)
            
            print(f"🔄 [{datetime.now().strftime('%H:%M:%S')}] Loading model for {MODEL_ID}...", 
                  file=sys.stderr, flush=True)
            self.model = AutoModelForSeq2SeqLM.from_pretrained(MODEL_ID, local_files_only=False)
            
            print(f"✅ [{datetime.now().strftime('%H:%M:%S')}] Model initialization complete! Ready for high-speed translation.", 
                  file=sys.stderr, flush=True)
            self.initialized = True
            return True
        except Exception as e:
            print(f"❌ [{datetime.now().strftime('%H:%M:%S')}] Model initialization failed: {e}", 
                  file=sys.stderr, flush=True)
            print(f"📄 Traceback:\n{traceback.format_exc()}", file=sys.stderr, flush=True)
            return False
    
    def translate(self, japanese_text):
        """
        日本語→英語翻訳（高速実行：0.1-0.5秒目標）
        """
        if not self.initialized:
            return {"success": False, "error": "Service not initialized"}
        
        start_time = time.time()
        
        try:
            # HuggingFace Transformers標準処理
            inputs = self.tokenizer(japanese_text, return_tensors="pt")
            
            with torch.no_grad():
                outputs = self.model.generate(
                    **inputs,
                    max_length=128,
                    num_beams=3,
                    length_penalty=1.0,
                    early_stopping=True
                )
            
            translation = self.tokenizer.decode(outputs[0], skip_special_tokens=True)
            
            processing_time = time.time() - start_time
            self.translation_count += 1
            
            print(f"⚡ [{datetime.now().strftime('%H:%M:%S')}] Translation #{self.translation_count} completed in {processing_time:.3f}s: '{japanese_text}' → '{translation}'", 
                  file=sys.stderr, flush=True)
            
            return {
                "success": True,
                "translation": translation,
                "source": japanese_text,
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
                "source": japanese_text,
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
                    japanese_text = request.get("text", "")
                    
                    if not japanese_text:
                        response = {"success": False, "error": "Empty text"}
                    else:
                        response = self.translate(japanese_text)
                    
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
    if not server.initialize_model():
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