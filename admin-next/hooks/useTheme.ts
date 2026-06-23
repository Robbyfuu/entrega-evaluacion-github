"use client";

import { useCallback, useEffect, useState } from "react";

// Applies the chosen theme to <html>, driving BOTH the shadcn `.dark` class
// (used by the migrated shell + shadcn primitives) and the legacy
// `[data-theme]` attribute (used by not-yet-migrated panel sections). Both
// stay in sync so the whole panel switches together. Persisted in localStorage.
function applyTheme(dark: boolean) {
  const root = document.documentElement;
  root.classList.toggle("dark", dark);
  root.setAttribute("data-theme", dark ? "dark" : "light");
}

export function useTheme() {
  const [isDark, setIsDark] = useState(false);

  useEffect(() => {
    const saved = typeof window !== "undefined" ? localStorage.getItem("theme") : null;
    const dark = saved === "dark";
    setIsDark(dark);
    applyTheme(dark);
  }, []);

  const toggle = useCallback(() => {
    setIsDark((prev) => {
      const next = !prev;
      applyTheme(next);
      try {
        localStorage.setItem("theme", next ? "dark" : "light");
      } catch {
        // ignore storage failures
      }
      return next;
    });
  }, []);

  return { isDark, toggle };
}
