import {
  CopilotRuntime,
  copilotRuntimeNextJSAppRouterEndpoint,
  ExperimentalEmptyAdapter,
} from "@copilotkit/runtime";

const BACKEND_URL = process.env.BACKEND_URL || "http://localhost:5000";

const runtime = new CopilotRuntime({
  remoteEndpoints: [
    {
      url: `${BACKEND_URL}/api/copilotkit`,
    },
  ],
});

export const POST = async (req: Request) => {
  const { handleRequest } = copilotRuntimeNextJSAppRouterEndpoint({
    runtime,
    serviceAdapter: new ExperimentalEmptyAdapter(),
    endpoint: "/api/copilotkit",
  });

  return handleRequest(req);
};
