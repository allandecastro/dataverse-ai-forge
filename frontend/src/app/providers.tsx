"use client";

import { CopilotKit } from "@copilotkit/react-core";
import { ThemeProvider } from "@/components/layout/ThemeProvider";
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
        {children}
      </CopilotKit>
    </ThemeProvider>
  );
}
