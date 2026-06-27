import { describe, it, expect } from "vitest";
import { safeHref } from "./url";

// safeHref es el guard anti-XSS de la auditoria (sec-panel-01): React NO bloquea
// javascript:/data: en href, asi que cualquier url de input de alumno se valida
// aca. Solo pasa http(s); el resto -> null (el <a> queda sin href navegable).
describe("safeHref", () => {
  it("deja pasar http(s)", () => {
    expect(safeHref("https://github.com/alumno/repo")).toBe("https://github.com/alumno/repo");
    expect(safeHref("http://example.com")).toBe("http://example.com");
  });

  it("es case-insensitive en el esquema", () => {
    expect(safeHref("HTTPS://github.com/x")).toBe("HTTPS://github.com/x");
  });

  it("recorta espacios al borde", () => {
    expect(safeHref("  https://github.com/x  ")).toBe("https://github.com/x");
  });

  it("bloquea javascript: (vector XSS)", () => {
    expect(safeHref("javascript:alert(document.cookie)")).toBeNull();
    expect(safeHref("  javascript:alert(1)")).toBeNull();
  });

  it("bloquea data: y otros esquemas no-http", () => {
    expect(safeHref("data:text/html,<script>alert(1)</script>")).toBeNull();
    expect(safeHref("vbscript:msgbox(1)")).toBeNull();
    expect(safeHref("file:///etc/passwd")).toBeNull();
  });

  it("bloquea relativos y vacíos/nulos", () => {
    expect(safeHref("/ruta/relativa")).toBeNull();
    expect(safeHref("github.com/x")).toBeNull();
    expect(safeHref("")).toBeNull();
    expect(safeHref(null)).toBeNull();
    expect(safeHref(undefined)).toBeNull();
  });
});
