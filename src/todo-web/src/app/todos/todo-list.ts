import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import {
  TODO_PRIORITIES,
  TODO_SORT_FIELDS,
  TODO_STATUSES,
  Todo,
  TodoPriority,
  TodoSortBy,
  TodoStatus,
} from '../models/todo';
import { TodoStore } from '../services/todo-store';

interface Draft {
  title: string;
  description: string;
  status: TodoStatus;
  priority: TodoPriority;
  dueDate: string;
}

const EMPTY_DRAFT: Draft = {
  title: '',
  description: '',
  status: 'Pending',
  priority: 'Medium',
  dueDate: '',
};

@Component({
  selector: 'app-todo-list',
  imports: [DatePipe],
  templateUrl: './todo-list.html',
  styleUrl: './todo-list.css',
})
export class TodoList {
  private readonly store = inject(TodoStore);

  protected readonly statuses = TODO_STATUSES;
  protected readonly priorities = TODO_PRIORITIES;
  protected readonly sortFields = TODO_SORT_FIELDS;

  protected readonly todos = this.store.todos;
  protected readonly pageInfo = this.store.pageInfo;
  protected readonly filters = this.store.filters;
  protected readonly loading = this.store.loading;
  protected readonly saving = this.store.saving;
  protected readonly loadError = this.store.loadError;
  protected readonly actionError = this.store.actionError;

  protected readonly composerOpen = signal(false);
  protected readonly draft = signal<Draft>({ ...EMPTY_DRAFT });
  protected readonly editingId = signal<string | null>(null);
  protected readonly editDraft = signal<Draft>({ ...EMPTY_DRAFT });

  /** Bound to the search box directly so typing stays responsive; the filter itself is debounced. */
  protected readonly searchText = signal(this.store.filters().search);
  private searchTimer?: ReturnType<typeof setTimeout>;

  protected readonly openCount = computed(
    () => this.todos().filter((t) => t.status !== 'Completed' && t.status !== 'Cancelled').length,
  );
  protected readonly overdueCount = computed(() => this.todos().filter((t) => t.isOverdue).length);

  protected readonly hasActiveFilters = computed(() => {
    const f = this.filters();
    return !!(f.status || f.priority || f.minPriority || f.search || f.overdueOnly);
  });

  protected onSearch(value: string): void {
    this.searchText.set(value);
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => this.store.patchFilters({ search: value }), 300);
  }

  protected setStatusFilter(value: string): void {
    this.store.patchFilters({ status: value as TodoStatus | '' });
  }

  protected setMinPriorityFilter(value: string): void {
    this.store.patchFilters({ minPriority: value as TodoPriority | '' });
  }

  protected setSort(value: string): void {
    this.store.patchFilters({ sortBy: value as TodoSortBy });
  }

  protected toggleDirection(): void {
    this.store.patchFilters({ descending: !this.filters().descending });
  }

  protected toggleOverdue(checked: boolean): void {
    this.store.patchFilters({ overdueOnly: checked });
  }

  protected clearFilters(): void {
    this.searchText.set('');
    this.store.resetFilters();
  }

  protected changePage(delta: number): void {
    this.store.goToPage(this.pageInfo().page + delta);
  }

  protected patchDraft(changes: Partial<Draft>): void {
    this.draft.update((d) => ({ ...d, ...changes }));
  }

  protected patchEditDraft(changes: Partial<Draft>): void {
    this.editDraft.update((d) => ({ ...d, ...changes }));
  }

  protected toggleComposer(): void {
    this.composerOpen.update((open) => !open);
    this.draft.set({ ...EMPTY_DRAFT });
  }

  protected async createTodo(): Promise<void> {
    const draft = this.draft();
    if (!draft.title.trim()) {
      return;
    }

    const created = await this.store.create({
      title: draft.title.trim(),
      description: draft.description.trim() || null,
      status: draft.status,
      priority: draft.priority,
      dueDate: toIso(draft.dueDate),
    });

    if (created) {
      this.draft.set({ ...EMPTY_DRAFT });
      this.composerOpen.set(false);
    }
  }

  protected startEdit(todo: Todo): void {
    this.editingId.set(todo.id);
    this.editDraft.set({
      title: todo.title,
      description: todo.description ?? '',
      status: todo.status,
      priority: todo.priority,
      dueDate: toLocalInput(todo.dueDate),
    });
  }

  protected cancelEdit(): void {
    this.editingId.set(null);
  }

  protected async saveEdit(id: string): Promise<void> {
    const draft = this.editDraft();
    if (!draft.title.trim()) {
      return;
    }

    const saved = await this.store.update(id, {
      title: draft.title.trim(),
      description: draft.description.trim() || null,
      status: draft.status,
      priority: draft.priority,
      dueDate: toIso(draft.dueDate),
    });

    if (saved) {
      this.editingId.set(null);
    }
  }

  protected setStatus(todo: Todo, value: string): void {
    void this.store.setStatus(todo.id, value as TodoStatus);
  }

  protected setPriority(todo: Todo, value: string): void {
    void this.store.setPriority(todo.id, value as TodoPriority);
  }

  /** One click to finish an item, or to put a finished one back in play. */
  protected toggleComplete(todo: Todo): void {
    void this.store.setStatus(todo.id, todo.status === 'Completed' ? 'Pending' : 'Completed');
  }

  protected async remove(todo: Todo): Promise<void> {
    if (confirm(`Delete "${todo.title}"?`)) {
      await this.store.remove(todo.id);
    }
  }

  protected trackById = (_: number, todo: Todo) => todo.id;
}

/** `datetime-local` gives a local wall-clock string; the API wants an instant. */
function toIso(value: string): string | null {
  if (!value) {
    return null;
  }
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? null : parsed.toISOString();
}

/** The inverse: an instant back into the `datetime-local` format, in local time. */
function toLocalInput(value: string | undefined): string {
  if (!value) {
    return '';
  }
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return '';
  }
  const pad = (n: number) => `${n}`.padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}
