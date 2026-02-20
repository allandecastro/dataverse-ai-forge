"use client";

import { CopilotKit } from "@copilotkit/react-core";
import { ThemeProvider } from "@/components/layout/ThemeProvider";
import type { ReactNode } from "react";

export function Providers({ children }: { children: ReactNode }) {
  return (
    <ThemeProvider>
      <CopilotKit runtimeUrl="/api/copilotkit" agent="dataverse_forge">
        {children}
      </CopilotKit>
    </ThemeProvider>
  );
}
