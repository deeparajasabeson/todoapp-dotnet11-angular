export type ChatRole = 'user' | 'assistant';

export interface ChatMessage {
  readonly role: ChatRole;
  readonly text: string;
  /** Specialists that handled an assistant turn, e.g. Scheduler, Analyst, Guide. */
  readonly agents?: readonly string[];
  readonly failed?: boolean;
}

export interface ChatReply {
  readonly conversationId: string;
  readonly reply: string;
  readonly agentsUsed: readonly string[];
  readonly changedData: boolean;
  readonly changedItemIds: readonly string[];
}

export interface ChatStatus {
  readonly available: boolean;
  readonly model?: string;
  readonly detail?: string;
}
