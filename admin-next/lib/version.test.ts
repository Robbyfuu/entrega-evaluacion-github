import { describe, it, expect } from "vitest";
import { compareVersions } from "./version";

describe("compareVersions", () => {
  it("mayor / menor / igual", () => {
    expect(compareVersions("2.7.20", "2.7.19")).toBeGreaterThan(0);
    expect(compareVersions("2.7.19", "2.7.20")).toBeLessThan(0);
    expect(compareVersions("2.7.0", "2.7.0")).toBe(0);
  });

  it("compara numéricamente, no lexicográficamente (10 > 9)", () => {
    expect(compareVersions("2.10.0", "2.9.0")).toBeGreaterThan(0);
    expect(compareVersions("2.7.20", "2.7.3")).toBeGreaterThan(0);
  });

  it("rellena longitudes distintas con 0 (2.7 == 2.7.0)", () => {
    expect(compareVersions("2.7", "2.7.0")).toBe(0);
    expect(compareVersions("2.7.1", "2.7")).toBeGreaterThan(0);
  });

  it("segmentos no numéricos (incl. prefijos parciales) cuentan como 0", () => {
    expect(compareVersions("2.7.x", "2.7.0")).toBe(0);
    expect(compareVersions("2.7.1", "2.7.x")).toBeGreaterThan(0);
    // Estricto: "1a" NO es 1 (a diferencia de parseInt) -> cuenta como 0.
    expect(compareVersions("2.7.1a", "2.7.0")).toBe(0);
    expect(compareVersions("2.7.2", "2.7.1a")).toBeGreaterThan(0);
  });

  it("diferencia en el segmento mayor domina", () => {
    expect(compareVersions("3.0.0", "2.99.99")).toBeGreaterThan(0);
  });
});
