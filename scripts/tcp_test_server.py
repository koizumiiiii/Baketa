#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
TCP Test Server for SmartConnectionEstablisher Phase 4.8 Testing
"""

import argparse
import json
import socket
import threading
import time

class SimpleTcpTestServer:
    def __init__(self, port=5557):
        self.port = port
        self.running = False
        self.server_socket = None
        
    def handle_client(self, client_socket, address):
        """Handle client connection and respond with test translation"""
        try:
            # Receive data
            data = client_socket.recv(4096).decode('utf-8')
            print(f"Received from {address}: {data}")
            import sys
            sys.stdout.flush()
            
            # Parse JSON request
            try:
                request = json.loads(data.strip())
                text = request.get('text', '')
                source_lang = request.get('source_lang', 'en')
                target_lang = request.get('target_lang', 'ja')
                
                # Create mock successful response
                response = {
                    "success": True,
                    "translation": f"Mock translation of '{text}'",
                    "processing_time": 0.1,
                    "source_lang": source_lang,
                    "target_lang": target_lang
                }
                
                print(f"Sending response: {response}")
                
            except json.JSONDecodeError:
                # Invalid JSON - return error
                response = {
                    "success": False,
                    "error": "Invalid JSON request"
                }
            
            # Send JSON response
            response_json = json.dumps(response, ensure_ascii=False) + '\n'
            client_socket.sendall(response_json.encode('utf-8'))
            
        except Exception as e:
            print(f"Error handling client {address}: {e}")
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
    
    def start_server(self):
        """Start TCP server"""
        self.server_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self.server_socket.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self.server_socket.bind(('127.0.0.1', self.port))
        self.server_socket.listen(5)
        
        self.running = True
        print(f"TCP Test Server listening on 127.0.0.1:{self.port}")
        print("Server is ready to accept connections...")
        import sys
        sys.stdout.flush()
        
        while self.running:
            try:
                client_socket, address = self.server_socket.accept()
                print(f"Connection from {address}")
                import sys
                sys.stdout.flush()
                
                # Handle each client in a separate thread
                client_thread = threading.Thread(
                    target=self.handle_client, 
                    args=(client_socket, address)
                )
                client_thread.daemon = True
                client_thread.start()
                
            except Exception as e:
                if self.running:
                    print(f"Accept error: {e}")
    
    def stop_server(self):
        """Stop server"""
        self.running = False
        if self.server_socket:
            self.server_socket.close()

def main():
    parser = argparse.ArgumentParser(description='TCP Test Server for Phase 4.8 Testing')
    parser.add_argument('--port', type=int, default=5557, help='Server port (default: 5557)')
    args = parser.parse_args()
    
    server = SimpleTcpTestServer(port=args.port)
    
    try:
        server.start_server()
    except KeyboardInterrupt:
        print("\nShutting down server...")
    finally:
        server.stop_server()

if __name__ == "__main__":
    main()