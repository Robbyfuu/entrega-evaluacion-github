import { describe, it, expect } from "vitest";
import { parseRoster } from "./roster";

const validStudent = {
  blackboard_student_id: "BB-001",
  full_name: "Ana Pérez",
  email: "ana@duocuc.cl",
  github_username: "ana-gh",
};

describe("parseRoster — happy path", () => {
  it("parsea un roster válido completo", () => {
    const roster = parseRoster({
      section: "001D",
      courseId: "FPY1101",
      students: [validStudent],
    });
    expect(roster.section).toBe("001D");
    expect(roster.courseId).toBe("FPY1101");
    expect(roster.students).toHaveLength(1);
    expect(roster.students[0]).toEqual(validStudent);
  });

  it("courseId ausente -> default ''", () => {
    const roster = parseRoster({ section: "001D", students: [validStudent] });
    expect(roster.courseId).toBe("");
  });

  it("email/github no-string o vacío -> null (coerción)", () => {
    const roster = parseRoster({
      section: "001D",
      students: [{ blackboard_student_id: "BB-002", full_name: "Sin Datos", email: 123, github_username: "" }],
    });
    expect(roster.students[0].email).toBeNull();
    expect(roster.students[0].github_username).toBeNull();
  });

  it("email vacío -> null (consistente con github_username)", () => {
    const roster = parseRoster({
      section: "001D",
      students: [{ blackboard_student_id: "BB-003", full_name: "Sin Email", email: "", github_username: "gh" }],
    });
    expect(roster.students[0].email).toBeNull();
    expect(roster.students[0].github_username).toBe("gh");
  });

  it("acepta lista de students vacía", () => {
    const roster = parseRoster({ section: "001D", students: [] });
    expect(roster.students).toEqual([]);
  });
});

describe("parseRoster — validaciones (hard-fail antes de tocar la DB)", () => {
  it.each([
    ["string", "no-es-objeto"],
    ["null", null],
    ["number", 42],
  ])("rechaza raw %s", (_label, raw) => {
    expect(() => parseRoster(raw)).toThrow(/no es un roster válido/);
  });

  it("rechaza section ausente/vacío", () => {
    expect(() => parseRoster({ students: [] })).toThrow(/"section"/);
    expect(() => parseRoster({ section: "", students: [] })).toThrow(/"section"/);
  });

  it("rechaza students que no es array", () => {
    expect(() => parseRoster({ section: "001D", students: "x" })).toThrow(/"students"/);
  });

  it("rechaza alumno que no es objeto", () => {
    expect(() => parseRoster({ section: "001D", students: ["x"] })).toThrow(/#1 no es un objeto/);
  });

  it("rechaza alumno sin blackboard_student_id", () => {
    expect(() =>
      parseRoster({ section: "001D", students: [{ full_name: "X" }] })
    ).toThrow(/#1 no tiene blackboard_student_id/);
  });

  it("rechaza alumno sin full_name", () => {
    expect(() =>
      parseRoster({ section: "001D", students: [{ blackboard_student_id: "BB" }] })
    ).toThrow(/#1 no tiene full_name/);
  });

  it("el índice del error refleja la posición (1-based)", () => {
    expect(() =>
      parseRoster({ section: "001D", students: [validStudent, { full_name: "X" }] })
    ).toThrow(/#2 no tiene blackboard_student_id/);
  });
});
