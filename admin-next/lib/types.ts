// Row types for every Supabase table the panel reads or writes.
// Field names mirror the original admin panel queries exactly.

export interface CourseRow {
  id: number;
  code: string;
  name: string;
  active: boolean;
  created_at: string | null;
}

export interface SectionRow {
  id: number;
  course_id: number;
  code: string;
  name: string;
  created_at: string | null;
}

export interface EvaluationRow {
  id: number;
  section_id: number;
  title: string;
  // Stable numeric handle per section (UNIQUE(section_id, number)). Nullable for
  // legacy rows that predate the column; the panel assigns it explicitly.
  number: number | null;
  classroom_url: string | null;
  org: string | null;
  active: boolean;
  created_at: string | null;
}

export interface ControlRow {
  id: number;
  internet_block: boolean;
  force_lockdown: boolean;
  message: string | null;
  updated_at: string | null;
  updated_by: string | null;
}

// Per-evaluation override over the global control id=1. A NULL field inherits
// the global value. Mirrors public.evaluation_control exactly (PR1 migration).
export interface EvaluationControlRow {
  evaluation_id: number;
  internet_block: boolean | null;
  force_lockdown: boolean | null;
  message: string | null;
  updated_at: string | null;
  updated_by: string | null;
}

export interface ClientProcess {
  name?: string | null;
  title?: string | null;
}

export interface OnlineClientRow {
  pc_name: string | null;
  github_username: string | null;
  section: string | null;
  section_id: number | null;
  // Which evaluation this client is sitting (PR1 column). Distinguishes
  // re-sittings of the same PC across different evaluations.
  evaluation_id: number | null;
  last_seen: string;
  processes: ClientProcess[] | null;
  internet_state: string | null; // 'blocked' | other
  lockdown_state: string | null; // 'active' | other
}

export interface ProcessAlertRow {
  id?: number | string;
  detected_at: string;
  github_username: string | null;
  pc_name: string | null;
  section: string | null;
  section_id: number | null;
  process_name: string;
  window_title: string | null;
}

export interface BrowserHistoryRow {
  id?: number | string;
  visited_at: string;
  github_username: string | null;
  pc_name: string | null;
  section: string | null;
  section_id: number | null;
  allowed: boolean;
  url: string | null;
}

export interface CheatEventRow {
  id?: number | string;
  detected_at: string;
  username: string | null;
  pc_name: string | null;
  repo_name: string | null;
  files_count: number | null;
  files_sample: string[] | null;
}

export interface AssignmentRow {
  id: number | string;
  title: string;
  section: string | null;
  org: string | null;
  classroom_url: string;
  active: boolean;
  evaluation_id: number | null;
  allows_manual_submission?: boolean;
  created_at?: string | null;
}

export interface AssignmentAcceptanceRow {
  id?: number | string;
  assignment_id: number | string;
  github_username: string | null;
  evaluation_id: number | null;
  section_id: number | null;
  accepted_at?: string | null;
}

export interface AssignmentSubmissionRow {
  id?: number | string;
  assignment_id: number | string;
  github_username: string;
  repo_url: string;
  status: string;
  submitted_at?: string | null;
}

export interface TargetedLockdownRow {
  pc_name: string;
  github_username: string;
  active: boolean;
  reason: string | null;
  released_at: string | null;
}

export interface SuspiciousProcess {
  id: number;
  process_name: string;
  section: string | null;
  section_id: number | null;
  created_at: string;
}

// Fila de la tabla `allowed_urls` (allowlist del navegador embebido).
// kind='domain' (match por sufijo de host) | 'exact_url' (match por prefijo).
// section=null => regla global.
export interface AllowedUrlRow {
  id: number;
  pattern: string;
  kind: "domain" | "exact_url";
  section: string | null;
  section_id: number | null;
  created_at: string;
}

export interface StudentActivityRow {
  id?: number | string;
  created_at: string;
  section: string | null;
  section_id: number | null;
  github_username: string | null;
  github_email: string | null;
  pc_name: string | null;
  action: string;
  repo_name: string | null;
  repo_url: string | null;
}

// Mirrors the enrollments table (roster imported from Blackboard).
// PII table: only the authenticated panel reads it. github_username is
// nullable until the teacher assigns it manually.
export interface EnrollmentRow {
  id: number;
  section_id: number;
  full_name: string;
  email: string | null;
  github_username: string | null;
  blackboard_student_id: string;
  status: string;
  created_at: string | null;
  updated_at: string | null;
}

// Mirrors the v_enrollment_status view (read-only cross-validation of the
// roster against acceptances/submissions). `source` separates roster rows
// from orphan activity and the "section sin resolver" bucket.
export interface EnrollmentStatusRow {
  source: "roster" | "orphan" | "unresolved_section";
  enrollment_id: number | null;
  section_id: number | null;
  full_name: string | null;
  email: string | null;
  github_username: string | null;
  status: string | null;
  github_resolved: boolean;
  accepted: boolean;
  submitted: boolean;
}
