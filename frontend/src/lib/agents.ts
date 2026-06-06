import {
  Layers,
  MessageSquare,
  Database,
  LayoutDashboard,
  Zap,
  Shield,
  BarChart3,
  TestTube,
  BookOpen,
  type LucideIcon,
} from "lucide-react";

/**
 * Visual metadata for an agent (icon + colour + display name). Named distinctly
 * from <c>agentDefinitions.ts</c>'s `AgentDefinition` (the orchestration-topology
 * shape) so the two can't be confused at an import site.
 */
export interface AgentVisual {
  name: string;
  displayName: string;
  icon: LucideIcon;
  color: string;
}

export const agents: Record<string, AgentVisual> = {
  SolutionArchitect: {
    name: "SolutionArchitect",
    displayName: "Solution Architect",
    icon: Layers,
    color: "var(--blue)",
  },
  BusinessAnalyst: {
    name: "BusinessAnalyst",
    displayName: "Business Analyst",
    icon: MessageSquare,
    color: "var(--purple)",
  },
  DataModelBuilder: {
    name: "DataModelBuilder",
    displayName: "Data Model Builder",
    icon: Database,
    color: "var(--green)",
  },
  UIBuilder: {
    name: "UIBuilder",
    displayName: "UI Builder",
    icon: LayoutDashboard,
    color: "var(--blue)",
  },
  AutomationBuilder: {
    name: "AutomationBuilder",
    displayName: "Automation Builder",
    icon: Zap,
    color: "var(--orange)",
  },
  SecurityBuilder: {
    name: "SecurityBuilder",
    displayName: "Security Builder",
    icon: Shield,
    color: "var(--red)",
  },
  DataPopulation: {
    name: "DataPopulation",
    displayName: "Data Population",
    icon: BarChart3,
    color: "var(--orange)",
  },
  Tester: {
    name: "Tester",
    displayName: "Tester",
    icon: TestTube,
    color: "var(--green)",
  },
  Documentor: {
    name: "Documentor",
    displayName: "Documentor",
    icon: BookOpen,
    color: "var(--t3)",
  },
};

export const defaultAgent = agents.SolutionArchitect;
