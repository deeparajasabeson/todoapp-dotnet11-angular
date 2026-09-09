import { Component, ElementRef, effect, inject, signal, viewChild } from '@angular/core';
import { ChatStore } from '../services/chat-store';

const SUGGESTIONS = [
  'What is overdue?',
  'Add a high priority task to renew my passport',
  'How many items are still open?',
  'When does an item count as overdue?',
];

@Component({
  selector: 'app-chat-panel',
  templateUrl: './chat-panel.html',
  styleUrl: './chat-panel.css',
})
export class ChatPanel {
  private readonly store = inject(ChatStore);

  protected readonly messages = this.store.messages;
  protected readonly sending = this.store.sending;
  protected readonly status = this.store.status;
  protected readonly available = this.store.available;
  protected readonly isEmpty = this.store.isEmpty;

  protected readonly draft = signal('');
  protected readonly suggestions = SUGGESTIONS;

  private readonly transcript = viewChild<ElementRef<HTMLElement>>('transcript');

  constructor() {
    // Follow the conversation as it grows, including while a reply is pending.
    effect(() => {
      this.messages();
      this.sending();

      const element = this.transcript()?.nativeElement;
      if (!element) {
        return;
      }

      queueMicrotask(() => {
        // scrollTo is absent in jsdom, so fall back to the property every DOM supports.
        if (typeof element.scrollTo === 'function') {
          element.scrollTo({ top: element.scrollHeight, behavior: 'smooth' });
        } else {
          element.scrollTop = element.scrollHeight;
        }
      });
    });
  }

  protected onKeydown(event: KeyboardEvent): void {
    // Enter sends; Shift+Enter is a newline, as in every other chat box.
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      void this.send();
    }
  }

  protected async send(): Promise<void> {
    const text = this.draft();
    if (!text.trim() || this.sending()) {
      return;
    }

    this.draft.set('');
    await this.store.send(text);
  }

  protected async useSuggestion(text: string): Promise<void> {
    if (this.sending()) {
      return;
    }
    await this.store.send(text);
  }

  protected reset(): void {
    this.store.reset();
    this.draft.set('');
  }
}
