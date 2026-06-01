// Solid badge colours, copied from the original panel so the look is identical.
export const BADGE = {
  user: "#0071e3", // GitHub username / section (activity & alerts)
  sectionAlt: "#673ab7", // section badge in online table / "all sections" assignment
  danger: "#c62828",
  success: "#4caf50",
  neutral: "#9e9e9e",
  lockdown: "#b71c1c",
} as const;

// Student activity action labels + colours (from the original ACTION_LABEL/COLOR).
export const ACTION_LABEL: Record<string, string> = {
  login: "Login",
  create_repo: "Crear repo",
  clone: "Clonar",
  upload: "Subir",
};

export const ACTION_COLOR: Record<string, string> = {
  login: "#0071e3",
  create_repo: "#4caf50",
  clone: "#ff9800",
  upload: "#9c27b0",
};
