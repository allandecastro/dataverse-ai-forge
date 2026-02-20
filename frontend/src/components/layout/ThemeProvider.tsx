"use client";

import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useState,
  type ReactNode,
} from "react";
import { darkTheme, lightTheme, type ThemeTokens } from "@/lib/themes";
import type { ThemeMode } from "@/lib/types";

interface ThemeContextValue {
  mode: ThemeMode;
  theme: ThemeTokens;
  toggleTheme: () => void;
}

const ThemeContext = createContext<ThemeContextValue>({
  mode: "dark",
  theme: darkTheme,
  toggleTheme: () => {},
});

export function useTheme() {
  return useContext(ThemeContext);
}

function applyThemeVars(theme: ThemeTokens) {
  const root = document.documentElement;
  for (const [key, value] of Object.entries(theme)) {
    root.style.setProperty(`--${key}`, value);
  }
}

function getInitialMode(): ThemeMode {
  if (typeof window !== "undefined") {
    const stored = localStorage.getItem("theme") as ThemeMode | null;
    if (stored === "light" || stored === "dark") return stored;
  }
  return "dark";
}

export function ThemeProvider({ children }: { children: ReactNode }) {
  const [mode, setMode] = useState<ThemeMode>(getInitialMode);

  useEffect(() => {
    const theme = mode === "dark" ? darkTheme : lightTheme;
    applyThemeVars(theme);
    localStorage.setItem("theme", mode);
  }, [mode]);

  const toggleTheme = useCallback(() => {
    setMode((prev) => (prev === "dark" ? "light" : "dark"));
  }, []);

  const theme = mode === "dark" ? darkTheme : lightTheme;

  return (
    <ThemeContext.Provider value={{ mode, theme, toggleTheme }}>
      {children}
    </ThemeContext.Provider>
  );
}
