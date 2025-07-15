#!/usr/bin/env node

/**
 * Google Gemini Web Search MCP Server for Baketa Project
 * 
 * This server provides web search capabilities using Google Gemini
 * to help with technical problem-solving during Baketa development.
 */

import { GoogleGenerativeAI } from '@google/generative-ai';
import { Server } from '@modelcontextprotocol/sdk/server/index.js';
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';
import {
  CallToolRequestSchema,
  ErrorCode,
  ListToolsRequestSchema,
  McpError,
} from '@modelcontextprotocol/sdk/types.js';

// Initialize Gemini client
const genAI = new GoogleGenerativeAI(process.env.GEMINI_API_KEY);

// Create MCP server
const server = new Server(
  {
    name: 'gemini-search-server',
    version: '1.0.0',
  },
  {
    capabilities: {
      tools: {},
    },
  }
);

// Tool definitions for Baketa development
const TOOLS = [
  {
    name: 'search_technical_solution',
    description: 'Search for technical solutions related to Baketa development (OCR, translation, Avalonia UI, etc.)',
    inputSchema: {
      type: 'object',
      properties: {
        query: {
          type: 'string',
          description: 'Technical problem or question to search for',
        },
        context: {
          type: 'string',
          description: 'Additional context about the Baketa project or specific component',
        },
        language: {
          type: 'string',
          enum: ['en', 'ja'],
          default: 'en',
          description: 'Language for search results',
        },
      },
      required: ['query'],
    },
  },
  {
    name: 'search_architecture_pattern',
    description: 'Search for architecture patterns and best practices for Clean Architecture, DI, MVVM, etc.',
    inputSchema: {
      type: 'object',
      properties: {
        pattern: {
          type: 'string',
          description: 'Architecture pattern or design principle to search for',
        },
        technology: {
          type: 'string',
          description: 'Specific technology stack (.NET, C#, Avalonia, etc.)',
        },
        problem: {
          type: 'string',
          description: 'Specific problem or challenge to solve',
        },
      },
      required: ['pattern'],
    },
  },
  {
    name: 'search_security_practices',
    description: 'Search for security best practices, CodeQL fixes, and privacy compliance',
    inputSchema: {
      type: 'object',
      properties: {
        security_area: {
          type: 'string',
          description: 'Security area to search for (exception handling, data protection, etc.)',
        },
        compliance: {
          type: 'string',
          description: 'Compliance standard (GDPR, CodeQL, etc.)',
        },
        technology: {
          type: 'string',
          description: 'Technology context (C#, .NET, Windows, etc.)',
        },
      },
      required: ['security_area'],
    },
  },
  {
    name: 'search_performance_optimization',
    description: 'Search for performance optimization techniques for real-time applications',
    inputSchema: {
      type: 'object',
      properties: {
        component: {
          type: 'string',
          description: 'Component to optimize (OCR, translation, UI, etc.)',
        },
        problem: {
          type: 'string',
          description: 'Performance issue or bottleneck',
        },
        platform: {
          type: 'string',
          default: 'windows',
          description: 'Target platform',
        },
      },
      required: ['component'],
    },
  },
];

// Handle list tools request
server.setRequestHandler(ListToolsRequestSchema, async () => {
  return {
    tools: TOOLS,
  };
});

// Handle tool calls
server.setRequestHandler(CallToolRequestSchema, async (request) => {
  const { name, arguments: args } = request.params;

  try {
    switch (name) {
      case 'search_technical_solution':
        return await searchTechnicalSolution(args);
      case 'search_architecture_pattern':
        return await searchArchitecturePattern(args);
      case 'search_security_practices':
        return await searchSecurityPractices(args);
      case 'search_performance_optimization':
        return await searchPerformanceOptimization(args);
      default:
        throw new McpError(ErrorCode.MethodNotFound, `Unknown tool: ${name}`);
    }
  } catch (error) {
    console.error('Tool execution error:', error);
    throw new McpError(ErrorCode.InternalError, `Tool execution failed: ${error.message}`);
  }
});

// Search functions
async function searchTechnicalSolution(args) {
  const { query, context = '', language = 'en' } = args;
  
  const systemPrompt = `You are a technical expert helping with Baketa development. 
Baketa is a Windows real-time game translation overlay application using:
- C# 12 / .NET 8.0
- Avalonia UI with ReactiveUI
- PaddleOCR for text recognition
- OpenCV for image processing
- OPUS-MT and Google Gemini for translation
- Clean Architecture with modular DI

Search for and provide detailed technical solutions for: "${query}"
${context ? `Context: ${context}` : ''}

Format response in ${language === 'ja' ? 'Japanese' : 'English'} with:
1. Problem analysis
2. Recommended solution
3. Code examples if applicable
4. Alternative approaches
5. Potential pitfalls`;

  const model = genAI.getGenerativeModel({ model: 'gemini-2.0-flash-exp' });
  const result = await model.generateContent({
    contents: [
      { role: 'user', parts: [{ text: systemPrompt + '\n\nSearch for: ' + query }] }
    ],
    generationConfig: {
      temperature: 0.1,
      maxOutputTokens: 2000,
    },
  });

  return {
    content: [
      {
        type: 'text',
        text: result.response.text(),
      },
    ],
  };
}

async function searchArchitecturePattern(args) {
  const { pattern, technology = '', problem = '' } = args;
  
  const systemPrompt = `You are a software architect expert specializing in Clean Architecture and modern .NET development.
  
Focus on: "${pattern}"
Technology: ${technology}
Problem: ${problem}

Provide architectural guidance for Baketa project including:
1. Pattern explanation and benefits
2. Implementation approach for .NET/C#
3. Integration with existing Clean Architecture
4. Code structure and organization
5. Testing considerations`;

  const model = genAI.getGenerativeModel({ model: 'gemini-2.0-flash-exp' });
  const result = await model.generateContent({
    contents: [
      { role: 'user', parts: [{ text: systemPrompt + '\n\nArchitecture pattern: ' + pattern }] }
    ],
    generationConfig: {
      temperature: 0.1,
      maxOutputTokens: 2000,
    },
  });

  return {
    content: [
      {
        type: 'text',
        text: result.response.text(),
      },
    ],
  };
}

async function searchSecurityPractices(args) {
  const { security_area, compliance = '', technology = 'C#' } = args;
  
  const systemPrompt = `You are a security expert specializing in secure software development.
  
Security Area: "${security_area}"
Compliance: ${compliance}
Technology: ${technology}

Provide security guidance for Baketa project including:
1. Security best practices
2. Code implementation examples
3. Common vulnerabilities to avoid
4. Compliance requirements
5. Testing and verification methods`;

  const model = genAI.getGenerativeModel({ model: 'gemini-2.0-flash-exp' });
  const result = await model.generateContent({
    contents: [
      { role: 'user', parts: [{ text: systemPrompt + '\n\nSecurity area: ' + security_area }] }
    ],
    generationConfig: {
      temperature: 0.1,
      maxOutputTokens: 2000,
    },
  });

  return {
    content: [
      {
        type: 'text',
        text: result.response.text(),
      },
    ],
  };
}

async function searchPerformanceOptimization(args) {
  const { component, problem = '', platform = 'windows' } = args;
  
  const systemPrompt = `You are a performance optimization expert for real-time applications.
  
Component: "${component}"
Problem: ${problem}
Platform: ${platform}

Provide performance optimization guidance for Baketa project including:
1. Performance bottleneck analysis
2. Optimization strategies
3. Implementation techniques
4. Monitoring and profiling
5. Platform-specific optimizations`;

  const model = genAI.getGenerativeModel({ model: 'gemini-2.0-flash-exp' });
  const result = await model.generateContent({
    contents: [
      { role: 'user', parts: [{ text: systemPrompt + '\n\nOptimize: ' + component }] }
    ],
    generationConfig: {
      temperature: 0.1,
      maxOutputTokens: 2000,
    },
  });

  return {
    content: [
      {
        type: 'text',
        text: result.response.text(),
      },
    ],
  };
}

// Start server
async function main() {
  const transport = new StdioServerTransport();
  await server.connect(transport);
  console.error('Gemini Search MCP Server running');
}

main().catch(console.error);