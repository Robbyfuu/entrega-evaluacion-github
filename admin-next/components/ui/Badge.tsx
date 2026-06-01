import type { CSSProperties, ReactNode } from "react";

interface BadgeProps {
  children: ReactNode;
  // Predefined token-based variants from the design system.
  variant?: "cheat" | "success" | "warn" | "info" | "neutral";
  // Solid coloured badge (white text on a custom background).
  solidColor?: string;
  style?: CSSProperties;
  className?: string;
}

const VARIANT_CLASS: Record<string, string> = {
  cheat: "badge badge-cheat",
  success: "badge badge-success",
  warn: "badge badge-warn",
  info: "badge badge-info",
  neutral: "badge badge-neutral",
};

export function Badge({ children, variant, solidColor, style, className }: BadgeProps) {
  if (solidColor) {
    return (
      <span
        className={`badge badge-solid ${className ?? ""}`}
        style={{ background: solidColor, ...style }}
      >
        {children}
      </span>
    );
  }
  return (
    <span className={`${VARIANT_CLASS[variant ?? "neutral"]} ${className ?? ""}`} style={style}>
      {children}
    </span>
  );
}
