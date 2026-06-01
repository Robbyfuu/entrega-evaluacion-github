"use client";

import { useCallback, useEffect, useState } from "react";

// Light/dark theme toggle persisted in localStorage and applied via
// [data-theme="dark"] on <html>, matching the original panel.
export function useTheme() {
  const [isDark, setIsDark] = useState(false);

  useEffect(() => {
    const saved = typeof window !== "undefined" ? localStorage.getItem("theme") : null;
    const dark = saved === "dark";
    setIsDark(dark);
    document.documentElement.setAttribute("data-theme", dark ? "dark" : "light");
  }, []);

  const toggle = useCallback(() => {
    setIsDark((prev) => {
      const next = !prev;
      document.documentElement.setAttribute("data-theme", next ? "dark" : "light");
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
