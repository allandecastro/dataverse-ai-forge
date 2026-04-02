"use client";

import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useSyncExternalStore,
  type ReactNode,
} from "react";
import { darkTheme, lightTheme, type ThemeTokens } from "@/lib/themes";
import { STORAGE_THEME } from "@/lib/constants";
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

// Listeners for useSyncExternalStore
let listeners: Array<() => void> = [];
function subscribe(cb: () => void) {
  listeners.push(cb);
  return () => { listeners = listeners.filter((l) => l !== cb); };
}
function getSnapshot(): ThemeMode {
  const stored = localStorage.getItem(STORAGE_THEME);
  return stored === "light" ? "light" : "dark";
}
function getServerSnapshot(): ThemeMode {
  return "dark"; // SSR always renders dark — prevents hydration mismatch
}

export function ThemeProvider({ children }: { children: ReactNode }) {
  const mode = useSyncExternalStore(subscribe, getSnapshot, getServerSnapshot);

  useEffect(() => {
    const theme = mode === "dark" ? darkTheme : lightTheme;
    applyThemeVars(theme);
  }, [mode]);

  const toggleTheme = useCallback(() => {
    const next: ThemeMode = getSnapshot() === "dark" ? "light" : "dark";
    localStorage.setItem(STORAGE_THEME, next);
    // Notify subscribers so useSyncExternalStore re-reads
    for (const cb of listeners) cb();
  }, []);

  const theme = mode === "dark" ? darkTheme : lightTheme;

  return (
    <ThemeContext.Provider value={{ mode, theme, toggleTheme }}>
      {children}
    </ThemeContext.Provider>
  );
}
