#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
Gemini API Code Review Script
Calls Gemini 2.0 Flash for code review
"""
import os
import sys
import io

# Force UTF-8 encoding for Windows
if sys.platform == 'win32':
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
    sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding='utf-8')

def main():
    # Get API key from environment
    api_key = os.environ.get('GEMINI_API_KEY')
    if not api_key:
        print("Error: GEMINI_API_KEY environment variable is not set")
        sys.exit(1)

    # Get prompt from command line
    if len(sys.argv) < 2:
        print("Usage: python gemini-review.py <prompt>")
        sys.exit(1)

    prompt = " ".join(sys.argv[1:])

    try:
        import google.generativeai as genai

        # Configure Gemini API
        genai.configure(api_key=api_key)

        # Use Gemini 2.5 Pro model (latest and most powerful)
        model = genai.GenerativeModel('gemini-2.5-pro')

        print(f"Sending prompt to Gemini API (model: gemini-2.5-pro)...")
        print(f"Prompt: {prompt}\n")

        # Generate response
        response = model.generate_content(prompt)

        print("Response from Gemini:\n")
        print(response.text)

        sys.exit(0)

    except ImportError:
        print("Error: google-generativeai package is not installed")
        print("Install with: pip install google-generativeai")
        sys.exit(1)
    except Exception as e:
        print(f"Error: {e}")
        sys.exit(1)

if __name__ == "__main__":
    main()
