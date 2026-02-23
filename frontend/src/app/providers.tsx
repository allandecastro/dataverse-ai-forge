"use client";

import { CopilotKit } from "@copilotkit/react-core";
import { ThemeProvider } from "@/components/layout/ThemeProvider";
import { ToastProvider } from "@/contexts/ToastContext";
import type { ReactNode } from "react";

interface ProvidersProps {
  children: ReactNode;
  threadId?: string;
}

export function Providers({ children, threadId }: ProvidersProps) {
  return (
    <ThemeProvider>
      <CopilotKit
        key={threadId}
        runtimeUrl="/api/copilotkit"
        agent="dataverse_forge"
        threadId={threadId}
      >
        <ToastProvider>{children}</ToastProvider>
      </CopilotKit>
    </ThemeProvider>
  );
}
