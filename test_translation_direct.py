#!/usr/bin/env python3
"""
Python翻訳サーバーへの直接接続テスト
"""
import socket
import json
import time
import sys

def test_translation_server():
    """翻訳サーバーへの直接テストを実行"""
    try:
        print("[INFO] Python翻訳サーバー直接接続テスト開始...")
        
        # TCP接続確立
        sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        sock.settimeout(10)  # 10秒タイムアウト
        
        print("[INFO] 127.0.0.1:5555に接続中...")
        sock.connect(('127.0.0.1', 5555))
        print("[SUCCESS] 接続成功")
        
        # テスト翻訳リクエスト
        test_request = {
            "text": "Hello world",
            "source_lang": "en",
            "target_lang": "ja",
            "request_id": "test-001"
        }
        
        # JSONリクエスト送信
        request_json = json.dumps(test_request)
        print(f"[SEND] 送信: {request_json}")
        
        sock.sendall((request_json + '\n').encode('utf-8'))
        
        # レスポンス受信
        print("[INFO] レスポンス受信中...")
        response_data = sock.recv(4096).decode('utf-8').strip()
        print(f"[RECV] 受信: {response_data}")
        
        # JSONパース
        try:
            response = json.loads(response_data)
            print(f"[SUCCESS] 翻訳成功: '{test_request['text']}' -> '{response.get('translation', 'なし')}'")
            print(f"[INFO] 成功フラグ: {response.get('success', False)}")
            print(f"[INFO] 処理時間: {response.get('processing_time', 0):.3f}秒")
            if response.get('error'):
                print(f"[ERROR] エラー: {response['error']}")
        except json.JSONDecodeError as e:
            print(f"[ERROR] JSONパースエラー: {e}")
            print(f"[DEBUG] 生データ: {response_data}")
        
        sock.close()
        return True
        
    except socket.timeout:
        print("[ERROR] 接続タイムアウト")
        return False
    except ConnectionRefusedError:
        print("[ERROR] 接続拒否 - サーバーが起動していない可能性")
        return False
    except Exception as e:
        print(f"[ERROR] エラー: {type(e).__name__}: {e}")
        return False
    finally:
        try:
            sock.close()
        except:
            pass

if __name__ == "__main__":
    success = test_translation_server()
    sys.exit(0 if success else 1)