// AI: побудова Zod-схеми з JSON Schema без eval (issue #62); перевірено: npm run typecheck, npm run lint, диференційне порівняння з json-schema-to-zod на схемах усіх 9 інструментів, самоперевірка автора (§8)

import { z } from 'zod';

/** Ключові слова JSON Schema, які генератор MCP-сервера переносить з OpenAPI-специфікації API. */
interface JsonSchema {
  type?: string | string[];
  enum?: unknown[];
  format?: string;
  minimum?: number;
  maximum?: number;
  minLength?: number;
  maxLength?: number;
  items?: unknown;
  minItems?: number;
  maxItems?: number;
  properties?: Record<string, unknown>;
  required?: string[];
  additionalProperties?: unknown;
}

type ZodOptions = [z.ZodTypeAny, z.ZodTypeAny, ...z.ZodTypeAny[]];

/**
 * Будує Zod-схему для перевірки аргументів інструмента безпосередньо з об'єкта JSON Schema.
 *
 * Згенерований сервер перетворював схему на текст програми (бібліотека json-schema-to-zod) і виконував
 * його через eval (CWE-95, правило security/detect-eval-with-expression). Тут код не генерується
 * й не виконується, тож підроблена схема може хіба що невдало перевірити аргументи.
 *
 * Підтримано ключові слова, які трапляються в схемах інструментів; решта ігнорується, тобто перевірка
 * стає м'якшою, а не суворішою. pattern пропущено свідомо: регулярний вираз зі специфікації — ризик
 * ReDoS, а формат значення однаково перевіряє API.
 */
export function jsonSchemaToZod(schema: unknown): z.ZodTypeAny {
  if (!isRecord(schema)) {
    // Булева схема: true приймає будь-яке значення, false — жодного
    return schema === false ? z.never() : z.any();
  }

  const jsonSchema = schema as JsonSchema;
  if (Array.isArray(jsonSchema.enum)) {
    return enumSchema(jsonSchema.enum);
  }

  const types = Array.isArray(jsonSchema.type)
    ? jsonSchema.type
    : jsonSchema.type === undefined
      ? []
      : [jsonSchema.type];
  const valueTypes = types.filter((type) => type !== 'null');
  if (valueTypes.length === 0) {
    return types.length > 0 ? z.null() : z.any();
  }

  const options = valueTypes.map((type) => typedSchema(type, jsonSchema));
  const result = options.length === 1 ? options[0] : z.union(options as ZodOptions);
  // OpenAPI 3.1 позначає необов'язкові значення як type: ["string", "null"]
  return valueTypes.length < types.length ? result.nullable() : result;
}

function typedSchema(type: string, schema: JsonSchema): z.ZodTypeAny {
  switch (type) {
    case 'string':
      return stringSchema(schema);
    case 'number':
    case 'integer':
      return numberSchema(schema, type === 'integer');
    case 'boolean':
      return z.boolean();
    case 'array':
      return arraySchema(schema);
    case 'object':
      return objectSchema(schema);
    default:
      return z.any();
  }
}

function stringSchema(schema: JsonSchema): z.ZodTypeAny {
  let result = z.string();
  switch (schema.format) {
    case 'uuid':
      result = result.uuid();
      break;
    case 'uri':
      result = result.url();
      break;
    case 'email':
      result = result.email();
      break;
    case 'date-time':
      result = result.datetime({ offset: true });
      break;
  }

  if (typeof schema.minLength === 'number') {
    result = result.min(schema.minLength);
  }
  if (typeof schema.maxLength === 'number') {
    result = result.max(schema.maxLength);
  }
  return result;
}

function numberSchema(schema: JsonSchema, integer: boolean): z.ZodTypeAny {
  let result = z.number();
  // Цілі параметри API приходять як type: number з format int32 або int64
  if (integer || schema.format === 'int32' || schema.format === 'int64') {
    result = result.int();
  }

  if (typeof schema.minimum === 'number') {
    result = result.min(schema.minimum);
  }
  if (typeof schema.maximum === 'number') {
    result = result.max(schema.maximum);
  }
  return result;
}

function arraySchema(schema: JsonSchema): z.ZodTypeAny {
  let result = z.array(jsonSchemaToZod(schema.items));
  if (typeof schema.minItems === 'number') {
    result = result.min(schema.minItems);
  }
  if (typeof schema.maxItems === 'number') {
    result = result.max(schema.maxItems);
  }
  return result;
}

function objectSchema(schema: JsonSchema): z.ZodTypeAny {
  const properties = isRecord(schema.properties) ? schema.properties : undefined;
  const required = new Set(Array.isArray(schema.required) ? schema.required : []);

  // Object.fromEntries створює власні властивості, тож ключ «__proto__» зі схеми не підмінить прототип
  const shape = Object.fromEntries(
    Object.entries(properties ?? {}).map(([key, property]) => {
      const propertySchema = jsonSchemaToZod(property);
      return [key, required.has(key) ? propertySchema : propertySchema.optional()];
    })
  );

  const result = z.object(shape);
  if (schema.additionalProperties === false) {
    return result.strict();
  }
  if (schema.additionalProperties !== undefined || properties === undefined) {
    // Словник (additionalProperties: {...}) або довільний об'єкт без properties зберігає всі поля
    return result.catchall(jsonSchemaToZod(schema.additionalProperties));
  }
  // Поля поза properties відкидаються: в API потрапляють лише параметри, описані в схемі
  return result;
}

function enumSchema(values: unknown[]): z.ZodTypeAny {
  if (values.length > 1 && values.every((value) => typeof value === 'string')) {
    return z.enum(values as [string, ...string[]]);
  }

  const literals: z.ZodTypeAny[] = values.filter(isLiteral).map((value) => z.literal(value));
  if (literals.length === 0) {
    return z.never();
  }
  return literals.length === 1 ? literals[0] : z.union(literals as ZodOptions);
}

function isLiteral(value: unknown): value is string | number | boolean | null {
  return (
    value === null ||
    typeof value === 'string' ||
    typeof value === 'number' ||
    typeof value === 'boolean'
  );
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
