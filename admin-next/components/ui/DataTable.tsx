import type { CSSProperties, ReactNode } from "react";

export interface Column<T> {
  header: ReactNode;
  // Cell renderer for a row.
  cell: (row: T) => ReactNode;
  // Optional <th> width style.
  width?: string;
}

interface DataTableProps<T> {
  columns: Column<T>[];
  rows: T[];
  getRowKey: (row: T, index: number) => string | number;
  loading?: boolean;
  error?: string | null;
  emptyMessage?: ReactNode;
  // Per-row extra props (className for blocked/flash rows, onClick, cursor...).
  rowProps?: (row: T) => {
    className?: string;
    onClick?: () => void;
    style?: CSSProperties;
  };
}

// Generic table that replaces the repeated tbody-building loops from the
// original panel. Handles loading / error / empty states centrally.
export function DataTable<T>({
  columns,
  rows,
  getRowKey,
  loading,
  error,
  emptyMessage = "Sin datos.",
  rowProps,
}: DataTableProps<T>) {
  const colCount = columns.length;

  let body: ReactNode;
  if (loading && rows.length === 0) {
    body = (
      <tr>
        <td colSpan={colCount} style={{ textAlign: "center", color: "var(--text-faint)" }}>
          Cargando...
        </td>
      </tr>
    );
  } else if (error) {
    body = (
      <tr>
        <td colSpan={colCount} className="err">
          Error: {error}
        </td>
      </tr>
    );
  } else if (rows.length === 0) {
    body = (
      <tr>
        <td colSpan={colCount} style={{ textAlign: "center", color: "var(--text-faint)" }}>
          {emptyMessage}
        </td>
      </tr>
    );
  } else {
    body = rows.map((row, index) => {
      const extra = rowProps?.(row) ?? {};
      return (
        <tr key={getRowKey(row, index)} {...extra}>
          {columns.map((col, ci) => (
            <td key={ci}>{col.cell(row)}</td>
          ))}
        </tr>
      );
    });
  }

  return (
    <table>
      <thead>
        <tr>
          {columns.map((col, i) => (
            <th key={i} style={col.width ? { width: col.width } : undefined}>
              {col.header}
            </th>
          ))}
        </tr>
      </thead>
      <tbody>{body}</tbody>
    </table>
  );
}
