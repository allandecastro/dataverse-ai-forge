"use client";

import { ThemeProvider } from "@/components/layout/ThemeProvider";
import { ToastProvider } from "@/contexts/ToastContext";
import { ForgeChatProvider } from "@/hooks/useForgeChat";
import type { ReactNode } from "react";

interface ProvidersProps {
  children: ReactNode;
  threadId?: string;
}

export function Providers({ children, threadId }: ProvidersProps) {
  return (
    <ThemeProvider>
      <ForgeChatProvider key={threadId} sessionId={threadId ?? ""}>
        <ToastProvider>{children}</ToastProvider>
      </ForgeChatProvider>
    </ThemeProvider>
  );
}
