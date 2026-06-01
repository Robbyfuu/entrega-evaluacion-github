import type { ReactNode } from "react";

interface SectionCardProps {
  id?: string;
  title: ReactNode;
  note?: ReactNode;
  children: ReactNode;
  footer?: ReactNode;
}

// Card wrapper used by every panel section. Mirrors the original `.card`.
export function SectionCard({ id, title, note, children, footer }: SectionCardProps) {
  return (
    <div className="card" id={id}>
      <h2>{title}</h2>
      {note ? <p className="muted-note">{note}</p> : null}
      {children}
      {footer}
    </div>
  );
}
