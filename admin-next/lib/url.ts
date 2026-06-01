// Returns the URL only if it uses a safe http(s) scheme, otherwise null.
// React does NOT block `javascript:` (or `data:`) in href, so any URL coming
// from student/teacher input must be validated before being rendered as a link.
export function safeHref(url: string | null | undefined): string | null {
  if (!url) return null;
  const trimmed = url.trim();
  if (/^https?:\/\//i.test(trimmed)) return trimmed;
  return null;
}
