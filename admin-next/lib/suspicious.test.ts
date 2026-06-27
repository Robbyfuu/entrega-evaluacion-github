// Estos vectores DEBEN coincidir con SuspiciousProcessesTests.cs (paridad
// cross-runtime); si divergen, una de las dos implementaciones rompio el contrato.
import { describe, it, expect } from "vitest";
import {
  normalizeProcessName,
  FALLBACK_SUSPICIOUS_PROCESSES,
} from "./suspicious";

// input -> expected (mismos casos que el [Theory] de SuspiciousProcessesTests.cs).
const NORMALIZE_VECTORS: ReadonlyArray<readonly [string, string]> = [
  ["Chrome.exe", "chrome"], // mayusculas + sufijo .exe
  ["CHROME.EXE", "chrome"], // .EXE en mayusculas (lower primero)
  ["  CODE  ", "code"], // espacios al borde + mayusculas
  [" Telegram.exe ", "telegram"], // espacios + .exe
  ["notepad++", "notepad++"], // ya normalizado, simbolos preservados
  ["chrome", "chrome"], // ya normalizado
  ["C:\\Tools\\AnyDesk.exe", "c:\\tools\\anydesk"], // ruta/path Windows
  ["my app .exe", "my app"], // espacio antes de .exe (trim final)
  [".exe", ""], // solo el sufijo -> vacio
  ["", ""], // vacio
  ["   ", ""], // solo whitespace
  // No-ASCII: latin acentuado concuerda byte-a-byte con ToLowerInvariant (.NET).
  // Chars patologicos (U+0130 I turca, U+1E9E ss) SI divergen entre runtimes y
  // quedan fuera de contrato (los nombres de proceso reales son ASCII/latin).
  ["Café.EXE", "café"], // acento preservado + .exe
];

describe("normalizeProcessName (paridad con C# SuspiciousProcesses.Normalize)", () => {
  it.each(NORMALIZE_VECTORS)("normaliza %j -> %j", (input, expected) => {
    expect(normalizeProcessName(input)).toBe(expected);
  });
});

describe("FALLBACK_SUSPICIOUS_PROCESSES (paridad con C# SuspiciousProcesses.Fallback)", () => {
  it("tiene la cantidad exacta de entradas", () => {
    expect(FALLBACK_SUSPICIOUS_PROCESSES.size).toBe(34);
  });

  it("contiene las entradas clave", () => {
    expect(FALLBACK_SUSPICIOUS_PROCESSES.has("chrome")).toBe(true);
    expect(FALLBACK_SUSPICIOUS_PROCESSES.has("copilot")).toBe(true);
    expect(FALLBACK_SUSPICIOUS_PROCESSES.has("notepad++")).toBe(true);
    expect(FALLBACK_SUSPICIOUS_PROCESSES.has("claude")).toBe(true);
  });
});
