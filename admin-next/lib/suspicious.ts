// Process names flagged as suspicious during an exam session.
// Copied verbatim from the original panel (browsers, messengers, alternate
// IDEs/editors, office apps, remote-access tools and AI assistants).
export const SUSPICIOUS_PROCESSES = new Set<string>([
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

export function isSuspicious(name?: string | null): boolean {
  return !!name && SUSPICIOUS_PROCESSES.has(name.toLowerCase());
}
