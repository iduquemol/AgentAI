export type MessageRole = "user" | "assistant";

export interface Message {
  id: string;
  role: MessageRole;
  content: string;
  createdAt: string;
}

export interface Conversation {
  id: string;
  agentId: number;
  startedAt: string;
  lastActivity: string;
  isActive: boolean;
}
