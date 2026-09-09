import { HttpClient, HttpErrorResponse, httpResource } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ChatMessage, ChatReply, ChatStatus } from '../models/chat';
import { ProblemDetails } from '../models/todo';
import { TodoStore } from './todo-store';

const API = '/api/chat';

/**
 * Conversation state for the assistant panel. The server owns the conversation history -
 * we hold an id and a local transcript for display, so a refresh starts a clean thread.
 */
@Injectable({ providedIn: 'root' })
export class ChatStore {
  private readonly http = inject(HttpClient);
  private readonly todos = inject(TodoStore);

  private readonly conversationId = signal<string | null>(null);

  readonly messages = signal<readonly ChatMessage[]>([]);
  readonly sending = signal(false);
  readonly error = signal<string | null>(null);

  private readonly statusResource = httpResource<ChatStatus>(() => `${API}/status`, {
    defaultValue: { available: false },
  });

  readonly status = computed(() => this.statusResource.value());
  readonly available = computed(() => this.status().available);
  readonly isEmpty = computed(() => this.messages().length === 0);

  async send(text: string): Promise<void> {
    const message = text.trim();
    if (!message || this.sending()) {
      return;
    }

    this.error.set(null);
    this.append({ role: 'user', text: message });
    this.sending.set(true);

    try {
      const reply = await firstValueFrom(
        this.http.post<ChatReply>(API, {
          message,
          conversationId: this.conversationId(),
        }),
      );

      this.conversationId.set(reply.conversationId);
      this.append({ role: 'assistant', text: reply.reply, agents: reply.agentsUsed });

      // The assistant writes through the same service the list reads, so anything it
      // changed is already in the database - just re-read.
      if (reply.changedData) {
        this.todos.refresh();
      }
    } catch (error) {
      const detail = describe(error);
      this.error.set(detail);
      this.append({ role: 'assistant', text: detail, failed: true });
    } finally {
      this.sending.set(false);
    }
  }

  /** Drops the transcript and the server-side thread it referred to. */
  reset(): void {
    this.conversationId.set(null);
    this.messages.set([]);
    this.error.set(null);
  }

  private append(message: ChatMessage): void {
    this.messages.update((current) => [...current, message]);
  }
}

function describe(error: unknown): string {
  if (!(error instanceof HttpErrorResponse)) {
    return 'Something went wrong talking to the assistant.';
  }

  if (error.status === 0) {
    return 'Could not reach the API. Is it running on port 5062?';
  }

  const problem = error.error as ProblemDetails | null;

  if (error.status === 503) {
    return problem?.detail ?? 'The assistant is not configured on the server.';
  }

  const fieldErrors = problem?.errors
    ? Object.values(problem.errors).flat()
    : [];

  return fieldErrors[0] ?? problem?.detail ?? problem?.title ?? `Request failed (${error.status}).`;
}
