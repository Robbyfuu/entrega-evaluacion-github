// Fallback set of suspicious process names, used when the live
// `suspicious_processes` table is empty or unavailable. Mirrors the client's
// fallback invariant so the highlight keeps working offline.
// Names are already normalized (lowercase, no `.exe`) to match
// `normalizeProcessName` below and the C# `Config.NormalizeProcessName`.
export const FALLBACK_SUSPICIOUS_PROCESSES = new Set<string>([
  "chrome",
  "msedge",
  "firefox",
  "opera",
  "brave",
  "iexplore",
  "vivaldi",
  "tor",
  "whatsapp",
  "discord",
  "telegram",
  "slack",
  "teams",
  "skype",
  "notion",
  "obsidian",
  "evernote",
  "onenote",
  "winword",
  "excel",
  "code",
  "pycharm",
  "pycharm64",
  "sublime_text",
  "notepad",
  "notepad++",
  "devenv",
  "anydesk",
  "teamviewer",
  "rustdesk",
  "msrdc",
  "chatgpt",
  "claude",
  "copilot",
]);

// Canonicalizes a process name for comparison. Must stay byte-for-byte
// equivalent to the C# `Config.NormalizeProcessName`:
//   1. null/empty/whitespace -> "".
//   2. trim.
//   3. lowercase.
//   4. strip a single trailing ".exe" (already lowercased, so an ordinary
//      ".exe" suffix; matched ordinally like the C# StringComparison.Ordinal).
//   5. trim again (in case a space preceded the ".exe").
export function normalizeProcessName(name: string): string {
  if (!name || !name.trim()) return "";
  let s = name.trim().toLowerCase();
  if (s.endsWith(".exe")) s = s.slice(0, -4);
  return s.trim();
}

export function isSuspicious(name?: string | null): boolean {
  return !!name && FALLBACK_SUSPICIOUS_PROCESSES.has(normalizeProcessName(name));
}
