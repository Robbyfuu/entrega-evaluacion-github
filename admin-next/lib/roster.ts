// Parseo y validación del roster (documento roster-{section}.json de bb-dl).
// Lógica pura, sin React/DOM — extraída de RosterImportSection para poder
// testearla aislada (lib/roster.test.ts).

// Shape of the bb-dl roster-{section}.json document (see bb-dl src/roster.ts).
export interface RosterFile {
  section: string;
  courseId: string;
  students: Array<{
    blackboard_student_id: string;
    full_name: string;
    email: string | null;
    github_username: string | null;
  }>;
}

// Validates the parsed JSON has the roster shape. Throws on a malformed file so
// the import hard-fails before touching the DB (never a partial/silent import).
export function parseRoster(raw: unknown): RosterFile {
  if (typeof raw !== "object" || raw === null) {
    throw new Error("El archivo no es un roster válido (no es un objeto JSON).");
  }
  const obj = raw as Record<string, unknown>;
  if (typeof obj.section !== "string" || !obj.section) {
    throw new Error('El roster no tiene un campo "section" válido.');
  }
  if (!Array.isArray(obj.students)) {
    throw new Error('El roster no tiene una lista "students".');
  }
  const students = obj.students.map((s, i) => {
    if (typeof s !== "object" || s === null) {
      throw new Error(`El alumno #${i + 1} no es un objeto válido.`);
    }
    const so = s as Record<string, unknown>;
    if (typeof so.blackboard_student_id !== "string" || !so.blackboard_student_id) {
      throw new Error(`El alumno #${i + 1} no tiene blackboard_student_id.`);
    }
    if (typeof so.full_name !== "string" || !so.full_name) {
      throw new Error(`El alumno #${i + 1} no tiene full_name.`);
    }
    return {
      blackboard_student_id: so.blackboard_student_id,
      full_name: so.full_name,
      // Email vacio -> null (consistente con github_username y con lo que espera
      // el downstream: el RPC y el placeholder de la UI tratan "sin email" como null).
      email: typeof so.email === "string" && so.email ? so.email : null,
      github_username:
        typeof so.github_username === "string" && so.github_username
          ? so.github_username
          : null,
    };
  });
  return {
    section: obj.section,
    courseId: typeof obj.courseId === "string" ? obj.courseId : "",
    students,
  };
}
