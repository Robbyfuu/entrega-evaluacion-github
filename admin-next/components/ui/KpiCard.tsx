import type { ReactNode } from "react";

interface KpiCardProps {
  label: string;
  value: ReactNode;
  variant?: "primary" | "danger" | "success" | "warning";
  small?: boolean;
}

export function KpiCard({ label, value, variant, small }: KpiCardProps) {
  const variantClass = variant ? ` is-${variant}` : "";
  return (
    <div className={`kpi${variantClass}`}>
      <div className="kpi-label">{label}</div>
      <div className="kpi-value" style={small ? { fontSize: 18 } : undefined}>
        {value}
      </div>
    </div>
  );
}
