// Row types for every Supabase table the panel reads or writes.
// Field names mirror the original admin panel queries exactly.

export interface ControlRow {
  id: number;
  internet_block: boolean;
  force_lockdown: boolean;
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
  process_name: string;
  window_title: string | null;
}

export interface BrowserHistoryRow {
  id?: number | string;
  visited_at: string;
  github_username: string | null;
  pc_name: string | null;
  section: string | null;
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
  created_at?: string | null;
}

export interface AssignmentAcceptanceRow {
  id?: number | string;
  assignment_id: number | string;
  github_username: string | null;
  accepted_at?: string | null;
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
  created_at: string;
}

export interface StudentActivityRow {
  id?: number | string;
  created_at: string;
  section: string | null;
  github_username: string | null;
  github_email: string | null;
  pc_name: string | null;
  action: string;
  repo_name: string | null;
  repo_url: string | null;
}
