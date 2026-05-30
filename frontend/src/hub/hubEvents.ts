export const HubEvents = {
  AgentThinking: "AgentThinking",
  AgentStream: "AgentStream",
  AgentToolCall: "AgentToolCall",
  AgentDone: "AgentDone",
} as const;

export type HubEventName = (typeof HubEvents)[keyof typeof HubEvents];
