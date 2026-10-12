#!/usr/bin/env node
/**
 * MCP Server generated from OpenAPI spec for waterfall-api vv1
 * Generated on: 2026-10-04T10:05:23.069Z
 */
// AI: побудова Zod-схеми аргументів без eval (issue #62); перевірено: npm run typecheck, npm run lint, диференційне порівняння з json-schema-to-zod, самоперевірка автора (§8)

// Load environment variables from .env file
import dotenv from 'dotenv';
dotenv.config();

import { Server } from "@modelcontextprotocol/sdk/server/index.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import {
  CallToolRequestSchema,
  ListToolsRequestSchema,
  type Tool,
  type CallToolResult,
  type CallToolRequest
} from "@modelcontextprotocol/sdk/types.js";

import { z, ZodError } from 'zod';
import { jsonSchemaToZod } from './jsonSchemaToZod.js';
import axios, { type AxiosRequestConfig, type AxiosError } from 'axios';

/**
 * Type definition for JSON objects
 */
type JsonObject = Record<string, any>;

/**
 * Interface for MCP Tool Definition
 */
interface McpToolDefinition {
    name: string;
    description: string;
    inputSchema: any;
    method: string;
    pathTemplate: string;
    executionParameters: { name: string, in: string }[];
    requestBodyContentType?: string;
    securityRequirements: any[];
    tags?: string[];
    deprecated?: boolean;
}

/**
 * Server configuration
 */
export const SERVER_NAME = "waterfall-api";
export const SERVER_VERSION = "v1";
// Base URL for the API, can be set via environment variable or determined from OpenAPI spec
export const API_BASE_URL = process.env.API_BASE_URL || "http://localhost:3000";
console.error("API_BASE_URL is set to:", API_BASE_URL);

/**
 * MCP Server instance
 */
const server = new Server(
    { name: SERVER_NAME, version: SERVER_VERSION },
    { capabilities: { tools: {} } }
);

/**
 * Map of tool definitions by name
 */
const toolDefinitionMap: Map<string, McpToolDefinition> = new Map([

  ["GetSavedVacancies", {
    name: "GetSavedVacancies",
    description: `Повертає перелік вакансій, які користувач зберіг («лайкнув») для подальшого відгуку чи рекомендацій ШІ.
(Tags: SavedVacancies)`,
    inputSchema: {"type":"object","properties":{}},
    method: "get",
    pathTemplate: "/api/v1/saved-vacancies",
    executionParameters: [],
    requestBodyContentType: undefined,
    securityRequirements: [],
    tags: ["SavedVacancies"],
    deprecated: false
  }],
  ["SaveVacancy", {
    name: "SaveVacancy",
    description: `Додає вакансію до списку вподобань.
(Tags: SavedVacancies)`,
    inputSchema: {"type":"object","properties":{"requestBody":{"required":["vacancy_id"],"type":"object","properties":{"vacancy_id":{"type":"string","description":"Унікальний ідентифікатор вакансії, яку потрібно додати у збережені.","format":"uuid"},"notes":{"maxLength":500,"minLength":0,"type":["string","null"],"description":"Необов'язкові власні нотатки користувача щодо цієї вакансії (до 500 символів)."}},"additionalProperties":false,"description":"Ідентифікатор вакансії та необов'язкові нотатки користувача."}}},
    method: "post",
    pathTemplate: "/api/v1/saved-vacancies",
    executionParameters: [],
    requestBodyContentType: "application/json",
    securityRequirements: [],
    tags: ["SavedVacancies"],
    deprecated: false
  }],
  ["GetSavedVacancyById", {
    name: "GetSavedVacancyById",
    description: `Повертає інформацію про збережену вакансію включно з нотатками шукача роботи.
(Tags: SavedVacancies)`,
    inputSchema: {"type":"object","properties":{"id":{"type":"string","format":"uuid","description":"Ідентифікатор запису збереженої вакансії."}},"required":["id"]},
    method: "get",
    pathTemplate: "/api/v1/saved-vacancies/{id}",
    executionParameters: [{"name":"id","in":"path"}],
    requestBodyContentType: undefined,
    securityRequirements: [],
    tags: ["SavedVacancies"],
    deprecated: false
  }],
  ["DeleteSavedVacancy", {
    name: "DeleteSavedVacancy",
    description: `Видаляє вказану вакансію зі списку вподобань користувача.
(Tags: SavedVacancies)`,
    inputSchema: {"type":"object","properties":{"id":{"type":"string","format":"uuid","description":"Унікальний ідентифікатор збереженої вакансії."}},"required":["id"]},
    method: "delete",
    pathTemplate: "/api/v1/saved-vacancies/{id}",
    executionParameters: [{"name":"id","in":"path"}],
    requestBodyContentType: undefined,
    securityRequirements: [],
    tags: ["SavedVacancies"],
    deprecated: false
  }],
  ["GetVacancies", {
    name: "GetVacancies",
    description: `Повертає список вакансій за заданими параметрами пошуку (назва, навички), фільтрами категорій,
мінімальної зарплати та джерела агрегації.
(Tags: Vacancies)`,
    inputSchema: {"type":"object","properties":{"page":{"maximum":2147483647,"minimum":1,"type":"number","format":"int32","description":"Номер запитуваної сторінки (починається з 1)."},"page_size":{"maximum":100,"minimum":1,"type":"number","format":"int32","description":"Кількість елементів на одній сторінці (від 1 до 100)."},"search":{"maxLength":100,"minLength":0,"type":"string","description":"Пошуковий запит (пошук за назвою посади, компанією або навичками)."},"category":{"maxLength":50,"minLength":0,"type":"string","description":"Фільтр за категорією/спеціалізацією (наприклад, .NET, QA, DevOps)."},"min_salary":{"maximum":1000000,"minimum":0,"type":"number","format":"double","description":"Фільтр за мінімальною заробітною платою."},"max_salary":{"maximum":1000000,"minimum":0,"type":"number","format":"double","description":"Фільтр за максимальною заробітною платою."},"experience":{"maxLength":50,"minLength":0,"type":"string","description":"Фільтр за необхідним досвідом (наприклад: 3+ роки, 1-3 роки)."},"source":{"enum":["WorkUa","RobotaUa","Djinni","Dou"],"type":"string","description":"Фільтр за джерелом агрегації (WorkUa, RobotaUa, Djinni, Dou)."}}},
    method: "get",
    pathTemplate: "/api/v1/vacancies",
    executionParameters: [{"name":"page","in":"query"},{"name":"page_size","in":"query"},{"name":"search","in":"query"},{"name":"category","in":"query"},{"name":"min_salary","in":"query"},{"name":"max_salary","in":"query"},{"name":"experience","in":"query"},{"name":"source","in":"query"}],
    requestBodyContentType: undefined,
    securityRequirements: [],
    tags: ["Vacancies"],
    deprecated: false
  }],
  ["CreateVacancy", {
    name: "CreateVacancy",
    description: `Додає нову вакансію до єдиного каталогу (ручне розміщення або збереження модулем збору даних).
(Tags: Vacancies)`,
    inputSchema: {"type":"object","properties":{"requestBody":{"required":["category","company","description","location","source","source_url","title"],"type":"object","properties":{"title":{"maxLength":150,"minLength":3,"type":"string","description":"Назва посади / вакансії (від 3 до 150 символів)."},"company":{"maxLength":100,"minLength":2,"type":"string","description":"Назва компанії-роботодавця (від 2 до 100 символів)."},"description":{"minLength":20,"type":"string","description":"Повний зміст опису та вимог до кандидата (щонайменше 20 символів)."},"category":{"maxLength":50,"minLength":2,"type":"string","description":"Професійна категорія вакансії."},"location":{"maxLength":100,"minLength":2,"type":"string","description":"Місце роботи або формат зайнятості."},"full_address":{"maxLength":200,"minLength":0,"type":["string","null"],"description":"Повна фізична адреса офісу чи локації"},"schedule":{"maxLength":100,"minLength":0,"type":["string","null"],"description":"Графік та тип зайнятості"},"experience":{"maxLength":50,"minLength":0,"type":["string","null"],"description":"Необхідний досвід роботи"},"salary_min":{"maximum":1000000,"minimum":0,"type":["number","null"],"description":"Мінімальна заробітна плата. Повинна бути більшою або рівною 0.","format":"double"},"salary_max":{"maximum":1000000,"minimum":0,"type":["number","null"],"description":"Максимальна заробітна плата. Повинна бути в межах від 0 до 1,000,000.","format":"double"},"currency":{"maxLength":3,"minLength":3,"type":["string","null"],"description":"Валюта заробітної плати (3 символи ISO, наприклад USD, EUR, UAH)."},"source":{"enum":["WorkUa","RobotaUa","Djinni","Dou"],"type":"string","description":"Джерело, з якого агреговано або розміщено вакансію."},"source_url":{"minLength":1,"type":"string","description":"Пряме посилання на першоджерело публікації.","format":"uri"},"skills":{"type":["array","null"],"items":{"type":"string"},"description":"Список ключових навичок."}},"additionalProperties":false,"description":"Дані нової вакансії."}}},
    method: "post",
    pathTemplate: "/api/v1/vacancies",
    executionParameters: [],
    requestBodyContentType: "application/json",
    securityRequirements: [],
    tags: ["Vacancies"],
    deprecated: false
  }],
  ["GetVacancyById", {
    name: "GetVacancyById",
    description: `Повертає повний опис, вимоги, рівень зарплати та контактні дані вакансії.
(Tags: Vacancies)`,
    inputSchema: {"type":"object","properties":{"id":{"type":"string","format":"uuid","description":"Унікальний глобальний ідентифікатор вакансії (GUID)."}},"required":["id"]},
    method: "get",
    pathTemplate: "/api/v1/vacancies/{id}",
    executionParameters: [{"name":"id","in":"path"}],
    requestBodyContentType: undefined,
    securityRequirements: [],
    tags: ["Vacancies"],
    deprecated: false
  }],
  ["UpdateVacancy", {
    name: "UpdateVacancy",
    description: `Оновлює інформацію про вакансію за її унікальним ідентифікатором.
(Tags: Vacancies)`,
    inputSchema: {"type":"object","properties":{"id":{"type":"string","format":"uuid","description":"Унікальний ідентифікатор вакансії, що оновлюється."},"requestBody":{"required":["category","company","description","location","title"],"type":"object","properties":{"title":{"maxLength":150,"minLength":3,"type":"string","description":"Оновлена назва посади (від 3 до 150 символів)."},"company":{"maxLength":100,"minLength":2,"type":"string","description":"Оновлена назва компанії (від 2 до 100 символів)."},"description":{"minLength":20,"type":"string","description":"Повний зміст опису та вимог."},"category":{"maxLength":50,"minLength":2,"type":"string","description":"Професійна категорія."},"location":{"maxLength":100,"minLength":2,"type":"string","description":"Локація або формат зайнятості."},"full_address":{"maxLength":200,"minLength":0,"type":["string","null"],"description":"Повна фізична адреса офісу чи локації."},"schedule":{"maxLength":100,"minLength":0,"type":["string","null"],"description":"Графік та тип зайнятості"},"experience":{"maxLength":50,"minLength":0,"type":["string","null"],"description":"Необхідний досвід роботи"},"salary_min":{"maximum":1000000,"minimum":0,"type":["number","null"],"description":"Мінімальна заробітна плата.","format":"double"},"salary_max":{"maximum":1000000,"minimum":0,"type":["number","null"],"description":"Максимальна заробітна плата.","format":"double"},"currency":{"maxLength":3,"minLength":3,"type":["string","null"],"description":"Валюта заробітної плати (3 символи ISO)."},"skills":{"type":["array","null"],"items":{"type":"string"},"description":"Список оновлених навичок."},"is_active":{"type":"boolean","description":"Статус активності вакансії."}},"additionalProperties":false,"description":"Оновлені поля вакансії."}},"required":["id"]},
    method: "put",
    pathTemplate: "/api/v1/vacancies/{id}",
    executionParameters: [{"name":"id","in":"path"}],
    requestBodyContentType: "application/json",
    securityRequirements: [],
    tags: ["Vacancies"],
    deprecated: false
  }],
  ["DeleteVacancy", {
    name: "DeleteVacancy",
    description: `Видаляє вакансію за її ідентифікатором з каталогу системи.
(Tags: Vacancies)`,
    inputSchema: {"type":"object","properties":{"id":{"type":"string","format":"uuid","description":"Унікальний ідентифікатор вакансії."}},"required":["id"]},
    method: "delete",
    pathTemplate: "/api/v1/vacancies/{id}",
    executionParameters: [{"name":"id","in":"path"}],
    requestBodyContentType: undefined,
    securityRequirements: [],
    tags: ["Vacancies"],
    deprecated: false
  }],
]);

/**
 * Security schemes from the OpenAPI spec
 */
const securitySchemes =   {};


server.setRequestHandler(ListToolsRequestSchema, async () => {
  const toolsForClient: Tool[] = Array.from(toolDefinitionMap.values()).map(def => ({
    name: def.name,
    description: def.description,
    inputSchema: def.inputSchema
  }));
  return { tools: toolsForClient };
});


server.setRequestHandler(CallToolRequestSchema, async (request: CallToolRequest): Promise<CallToolResult> => {
  const { name: toolName, arguments: toolArgs } = request.params;
  const toolDefinition = toolDefinitionMap.get(toolName);
  if (!toolDefinition) {
    console.error(`Error: Unknown tool requested: ${toolName}`);
    return { content: [{ type: "text", text: `Error: Unknown tool requested: ${toolName}` }] };
  }
  return await executeApiTool(toolName, toolDefinition, toolArgs ?? {}, securitySchemes);
});



/**
 * Type definition for cached OAuth tokens
 */
interface TokenCacheEntry {
    token: string;
    expiresAt: number;
}

/**
 * Declare global __oauthTokenCache property for TypeScript
 */
declare global {
    var __oauthTokenCache: Record<string, TokenCacheEntry> | undefined;
}

/**
 * Acquires an OAuth2 token using client credentials flow
 * 
 * @param schemeName Name of the security scheme
 * @param scheme OAuth2 security scheme
 * @returns Acquired token or null if unable to acquire
 */
async function acquireOAuth2Token(schemeName: string, scheme: any): Promise<string | null | undefined> {
    try {
        // Check if we have the necessary credentials (resolved per-scheme at runtime)
        const clientId = process.env[`OAUTH_CLIENT_ID_${schemeName.replace(/[^a-zA-Z0-9]/g, '_').toUpperCase()}`];
        const clientSecret = process.env[`OAUTH_CLIENT_SECRET_${schemeName.replace(/[^a-zA-Z0-9]/g, '_').toUpperCase()}`];
        const scopes = process.env[`OAUTH_SCOPES_${schemeName.replace(/[^a-zA-Z0-9]/g, '_').toUpperCase()}`];

        if (!clientId || !clientSecret) {
            console.error(`Missing client credentials for OAuth2 scheme '${schemeName}'`);
            return null;
        }
        
        // Initialize token cache if needed
        if (typeof global.__oauthTokenCache === 'undefined') {
            global.__oauthTokenCache = {};
        }
        
        // Check if we have a cached token
        const cacheKey = `${schemeName}_${clientId}`;
        const cachedToken = global.__oauthTokenCache[cacheKey];
        const now = Date.now();
        
        if (cachedToken && cachedToken.expiresAt > now) {
            console.error(`Using cached OAuth2 token for '${schemeName}' (expires in ${Math.floor((cachedToken.expiresAt - now) / 1000)} seconds)`);
            return cachedToken.token;
        }
        
        // Determine token URL based on flow type
        let tokenUrl = '';
        if (scheme.flows?.clientCredentials?.tokenUrl) {
            tokenUrl = scheme.flows.clientCredentials.tokenUrl;
            console.error(`Using client credentials flow for '${schemeName}'`);
        } else if (scheme.flows?.password?.tokenUrl) {
            tokenUrl = scheme.flows.password.tokenUrl;
            console.error(`Using password flow for '${schemeName}'`);
        } else {
            console.error(`No supported OAuth2 flow found for '${schemeName}'`);
            return null;
        }
        
        // Prepare the token request
        let formData = new URLSearchParams();
        formData.append('grant_type', 'client_credentials');
        
        // Add scopes if specified
        if (scopes) {
            formData.append('scope', scopes);
        }

        console.error(`Requesting OAuth2 token from ${tokenUrl}`);

        // Make the token request
        const response = await axios({
            method: 'POST',
            url: tokenUrl,
            headers: {
                'Content-Type': 'application/x-www-form-urlencoded',
                'Authorization': `Basic ${Buffer.from(`${clientId}:${clientSecret}`).toString('base64')}`
            },
            data: formData.toString()
        });
        
        // Process the response
        if (response.data?.access_token) {
            const token = response.data.access_token;
            const expiresIn = response.data.expires_in || 3600; // Default to 1 hour
            
            // Cache the token
            global.__oauthTokenCache[cacheKey] = {
                token,
                expiresAt: now + (expiresIn * 1000) - 60000 // Expire 1 minute early
            };
            
            console.error(`Successfully acquired OAuth2 token for '${schemeName}' (expires in ${expiresIn} seconds)`);
            return token;
        } else {
            console.error(`Failed to acquire OAuth2 token for '${schemeName}': No access_token in response`);
            return null;
        }
    } catch (error: unknown) {
        const errorMessage = error instanceof Error ? error.message : String(error);
        console.error(`Error acquiring OAuth2 token for '${schemeName}':`, errorMessage);
        return null;
    }
}


/**
 * Executes an API tool with the provided arguments
 * 
 * @param toolName Name of the tool to execute
 * @param definition Tool definition
 * @param toolArgs Arguments provided by the user
 * @param allSecuritySchemes Security schemes from the OpenAPI spec
 * @returns Call tool result
 */
async function executeApiTool(
    toolName: string,
    definition: McpToolDefinition,
    toolArgs: JsonObject,
    allSecuritySchemes: Record<string, any>
): Promise<CallToolResult> {
  try {
    // Validate arguments against the input schema
    let validatedArgs: JsonObject;
    try {
        const zodSchema = getZodSchemaFromJsonSchema(definition.inputSchema, toolName);
        const argsToParse = (typeof toolArgs === 'object' && toolArgs !== null) ? toolArgs : {};
        validatedArgs = zodSchema.parse(argsToParse);
    } catch (error: unknown) {
        if (error instanceof ZodError) {
            const validationErrorMessage = `Invalid arguments for tool '${toolName}': ${error.errors.map(e => `${e.path.join('.')} (${e.code}): ${e.message}`).join(', ')}`;
            return { content: [{ type: 'text', text: validationErrorMessage }] };
        } else {
             const errorMessage = error instanceof Error ? error.message : String(error);
             return { content: [{ type: 'text', text: `Internal error during validation setup: ${errorMessage}` }] };
        }
    }

    // Prepare URL, query parameters, headers, and request body
    let urlPath = definition.pathTemplate;
    const queryParams: Record<string, any> = {};
    const headers: Record<string, string> = { 'Accept': 'application/json' };
    let requestBodyData: any = undefined;

    // Apply parameters to the URL path, query, or headers
    definition.executionParameters.forEach((param) => {
        const value = validatedArgs[param.name];
        if (typeof value !== 'undefined' && value !== null) {
            if (param.in === 'path') {
                urlPath = urlPath.replace(`{${param.name}}`, encodeURIComponent(String(value)));
            }
            else if (param.in === 'query') {
                queryParams[param.name] = value;
            }
            else if (param.in === 'header') {
                headers[param.name.toLowerCase()] = String(value);
            }
        }
    });

    // Ensure all path parameters are resolved
    if (urlPath.includes('{')) {
        throw new Error(`Failed to resolve path parameters: ${urlPath}`);
    }
    
    // Construct the full URL
    const requestUrl = API_BASE_URL ? `${API_BASE_URL}${urlPath}` : urlPath;

    // Handle request body if needed
    if (definition.requestBodyContentType && typeof validatedArgs['requestBody'] !== 'undefined') {
        requestBodyData = validatedArgs['requestBody'];
        headers['content-type'] = definition.requestBodyContentType;
    }

    // Apply security requirements if available
    // Security requirements use OR between array items and AND within each object
    const appliedSecurity = definition.securityRequirements?.find(req => {
        // Try each security requirement (combined with OR)
        return Object.entries(req).every(([schemeName, scopesArray]) => {
            const scheme = allSecuritySchemes[schemeName];
            if (!scheme) return false;
            
            // API Key security (header, query, cookie)
            if (scheme.type === 'apiKey') {
                return !!process.env[`API_KEY_${schemeName.replace(/[^a-zA-Z0-9]/g, '_').toUpperCase()}`];
            }
            
            // HTTP security (basic, bearer)
            if (scheme.type === 'http') {
                if (scheme.scheme?.toLowerCase() === 'bearer') {
                    return !!process.env[`BEARER_TOKEN_${schemeName.replace(/[^a-zA-Z0-9]/g, '_').toUpperCase()}`];
                }
                else if (scheme.scheme?.toLowerCase() === 'basic') {
                    // Username is sufficient; an empty password is valid per RFC 7617 (issue #66)
                    return process.env[`BASIC_USERNAME_${schemeName.replace(/[^a-zA-Z0-9]/g, '_').toUpperCase()}`] != null;
                }
            }
            
            // OAuth2 security
            if (scheme.type === 'oauth2') {
                // Check for pre-existing token
                if (process.env[`OAUTH_TOKEN_${schemeName.replace(/[^a-zA-Z0-9]/g, '_').toUpperCase()}`]) {
                    return true;
                }
                
                // Check for client credentials for auto-acquisition
                if (process.env[`OAUTH_CLIENT_ID_${schemeName.replace(/[^a-zA-Z0-9]/g, '_').toUpperCase()}`] &&
                    process.env[`OAUTH_CLIENT_SECRET_${schemeName.replace(/[^a-zA-Z0-9]/g, '_').toUpperCase()}`]) {
                    // Verify we have a supported flow
                    if (scheme.flows?.clientCredentials || scheme.flows?.password) {
                        return true;
                    }
                }
                
                return false;
            }
            
            // OpenID Connect
            if (scheme.type === 'openIdConnect') {
                return !!process.env[`OPENID_TOKEN_${schemeName.replace(/[^a-zA-Z0-9]/g, '_').toUpperCase()}`];
            }
            
            return false;
        });
    });

    // If we found matching security scheme(s), apply them
    if (appliedSecurity) {
        // Apply each security scheme from this requirement (combined with AND)
        for (const [schemeName, scopesArray] of Object.entries(appliedSecurity)) {
            const scheme = allSecuritySchemes[schemeName];
            
            // API Key security
            if (scheme?.type === 'apiKey') {
                const apiKey = process.env[`API_KEY_${schemeName.replace(/[^a-zA-Z0-9]/g, '_').toUpperCase()}`];
                if (apiKey) {
                    if (scheme.in === 'header') {
                        headers[scheme.name.toLowerCase()] = apiKey;
                        console.error(`Applied API key '${schemeName}' in header '${scheme.name}'`);
                    }
                    else if (scheme.in === 'query') {
                        queryParams[scheme.name] = apiKey;
                        console.error(`Applied API key '${schemeName}' in query parameter '${scheme.name}'`);
                    }
                    else if (scheme.in === 'cookie') {
                        // Add the cookie, preserving other cookies if they exist
                        headers['cookie'] = `${scheme.name}=${apiKey}${headers['cookie'] ? `; ${headers['cookie']}` : ''}`;
                        console.error(`Applied API key '${schemeName}' in cookie '${scheme.name}'`);
                    }
                }
            } 
            // HTTP security (Bearer or Basic)
            else if (scheme?.type === 'http') {
                if (scheme.scheme?.toLowerCase() === 'bearer') {
                    const token = process.env[`BEARER_TOKEN_${schemeName.replace(/[^a-zA-Z0-9]/g, '_').toUpperCase()}`];
                    if (token) {
                        headers['authorization'] = `Bearer ${token}`;
                        console.error(`Applied Bearer token for '${schemeName}'`);
                    }
                } 
                else if (scheme.scheme?.toLowerCase() === 'basic') {
                    const username = process.env[`BASIC_USERNAME_${schemeName.replace(/[^a-zA-Z0-9]/g, '_').toUpperCase()}`];
                    const password = process.env[`BASIC_PASSWORD_${schemeName.replace(/[^a-zA-Z0-9]/g, '_').toUpperCase()}`];
                    // Empty password is valid per RFC 7617 (issue #66); only username is required.
                    if (username != null) {
                        headers['authorization'] = `Basic ${Buffer.from(`${username}:${password ?? ''}`).toString('base64')}`;
                        console.error(`Applied Basic authentication for '${schemeName}'`);
                    }
                }
            }
            // OAuth2 security
            else if (scheme?.type === 'oauth2') {
                // First try to use a pre-provided token
                let token = process.env[`OAUTH_TOKEN_${schemeName.replace(/[^a-zA-Z0-9]/g, '_').toUpperCase()}`];
                
                // If no token but we have client credentials, try to acquire a token
                if (!token && (scheme.flows?.clientCredentials || scheme.flows?.password)) {
                    console.error(`Attempting to acquire OAuth token for '${schemeName}'`);
                    token = (await acquireOAuth2Token(schemeName, scheme)) ?? '';
                }
                
                // Apply token if available
                if (token) {
                    headers['authorization'] = `Bearer ${token}`;
                    console.error(`Applied OAuth2 token for '${schemeName}'`);
                    
                    // List the scopes that were requested, if any
                    const scopes = scopesArray as string[];
                    if (scopes && scopes.length > 0) {
                        console.error(`Requested scopes: ${scopes.join(', ')}`);
                    }
                }
            }
            // OpenID Connect
            else if (scheme?.type === 'openIdConnect') {
                const token = process.env[`OPENID_TOKEN_${schemeName.replace(/[^a-zA-Z0-9]/g, '_').toUpperCase()}`];
                if (token) {
                    headers['authorization'] = `Bearer ${token}`;
                    console.error(`Applied OpenID Connect token for '${schemeName}'`);
                    
                    // List the scopes that were requested, if any
                    const scopes = scopesArray as string[];
                    if (scopes && scopes.length > 0) {
                        console.error(`Requested scopes: ${scopes.join(', ')}`);
                    }
                }
            }
        }
    } 
    // Log warning if security is required but not available
    else if (definition.securityRequirements?.length > 0) {
        // First generate a more readable representation of the security requirements
        const securityRequirementsString = definition.securityRequirements
            .map(req => {
                const parts = Object.entries(req)
                    .map(([name, scopesArray]) => {
                        const scopes = scopesArray as string[];
                        if (scopes.length === 0) return name;
                        return `${name} (scopes: ${scopes.join(', ')})`;
                    })
                    .join(' AND ');
                return `[${parts}]`;
            })
            .join(' OR ');
            
        console.warn(`Tool '${toolName}' requires security: ${securityRequirementsString}, but no suitable credentials found.`);
    }
    

    // Prepare the axios request configuration
    const config: AxiosRequestConfig = {
      method: definition.method.toUpperCase(),
      url: requestUrl,
      params: queryParams,
      headers: headers,
      // Serialize array query params as comma-separated values (issue #41)
      paramsSerializer: (params: Record<string, any>) => {
        const search = new URLSearchParams();
        for (const [key, value] of Object.entries(params)) {
          if (value === undefined || value === null) continue;
          search.append(key, Array.isArray(value) ? value.join(',') : String(value));
        }
        return search.toString();
      },
      ...(requestBodyData !== undefined && { data: requestBodyData }),
    };

    // Log request info to stderr (doesn't affect MCP output)
    console.error(`Executing tool "${toolName}": ${config.method} ${config.url}`);

    // Execute the request
    const response = await axios(config);

    // Process and format the response
    let responseText = '';
    // Coerce header value to string before lowercasing (issue #65)
    const contentType = String(response.headers['content-type'] ?? '').toLowerCase();
    
    // Handle JSON responses
    if (contentType.includes('application/json') && typeof response.data === 'object' && response.data !== null) {
         try { 
             responseText = JSON.stringify(response.data, null, 2); 
         } catch (e) { 
             responseText = "[Stringify Error]"; 
         }
    } 
    // Handle string responses
    else if (typeof response.data === 'string') { 
         responseText = response.data; 
    }
    // Handle other response types
    else if (response.data !== undefined && response.data !== null) { 
         responseText = String(response.data); 
    }
    // Handle empty responses
    else { 
         responseText = `(Status: ${response.status} - No body content)`; 
    }
    
    // Return formatted response
    return { 
        content: [ 
            { 
                type: "text", 
                text: `API Response (Status: ${response.status}):\n${responseText}` 
            } 
        ], 
    };

  } catch (error: unknown) {
    // Handle errors during execution
    let errorMessage: string;
    
    // Format Axios errors specially
    if (axios.isAxiosError(error)) { 
        errorMessage = formatApiError(error); 
    }
    // Handle standard errors
    else if (error instanceof Error) { 
        errorMessage = error.message; 
    }
    // Handle unexpected error types
    else { 
        errorMessage = 'Unexpected error: ' + String(error); 
    }
    
    // Log error to stderr
    console.error(`Error during execution of tool '${toolName}':`, errorMessage);
    
    // Return error message to client
    return { content: [{ type: "text", text: errorMessage }] };
  }
}


/**
 * Main function to start the server
 */
async function main() {
// Set up stdio transport
  try {
    const transport = new StdioServerTransport();
    await server.connect(transport);
    console.error(`${SERVER_NAME} MCP Server (v${SERVER_VERSION}) running on stdio${API_BASE_URL ? `, proxying API at ${API_BASE_URL}` : ''}`);
  } catch (error) {
    console.error("Error during server startup:", error);
    process.exit(1);
  }
}

/**
 * Cleanup function for graceful shutdown
 */
async function cleanup() {
    console.error("Shutting down MCP server...");
    process.exit(0);
}

// Register signal handlers
process.on('SIGINT', cleanup);
process.on('SIGTERM', cleanup);

// Start the server
main().catch((error) => {
  console.error("Fatal error in main execution:", error);
  process.exit(1);
});

/**
 * Formats API errors for better readability
 * 
 * @param error Axios error
 * @returns Formatted error message
 */
function formatApiError(error: AxiosError): string {
    let message = 'API request failed.';
    if (error.response) {
        message = `API Error: Status ${error.response.status} (${error.response.statusText || 'Status text not available'}). `;
        const responseData = error.response.data;
        const MAX_LEN = 200;
        if (typeof responseData === 'string') { 
            message += `Response: ${responseData.substring(0, MAX_LEN)}${responseData.length > MAX_LEN ? '...' : ''}`; 
        }
        else if (responseData) { 
            try { 
                const jsonString = JSON.stringify(responseData); 
                message += `Response: ${jsonString.substring(0, MAX_LEN)}${jsonString.length > MAX_LEN ? '...' : ''}`; 
            } catch { 
                message += 'Response: [Could not serialize data]'; 
            } 
        }
        else { 
            message += 'No response body received.'; 
        }
    } else if (error.request) {
        message = 'API Network Error: No response received from server.';
        if (error.code) message += ` (Code: ${error.code})`;
    } else { 
        message += `API Request Setup Error: ${error.message}`; 
    }
    return message;
}

/**
 * Converts a JSON Schema to a Zod schema for runtime validation
 * 
 * @param jsonSchema JSON Schema
 * @param toolName Tool name for error reporting
 * @returns Zod schema
 */
function getZodSchemaFromJsonSchema(jsonSchema: any, toolName: string): z.ZodTypeAny {
    if (typeof jsonSchema !== 'object' || jsonSchema === null) { 
        return z.object({}).passthrough(); 
    }
    try {
        // Build the schema object directly: no generated code is passed to eval (CWE-95)
        return jsonSchemaToZod(jsonSchema);
    } catch (err: any) {
        console.error(`Failed to build Zod schema for '${toolName}':`, err);
        return z.object({}).passthrough();
    }
}
