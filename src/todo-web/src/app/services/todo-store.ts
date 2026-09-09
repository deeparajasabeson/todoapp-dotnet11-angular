import { HttpClient, HttpErrorResponse, httpResource } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import {
  CreateTodoRequest,
  DEFAULT_FILTERS,
  PagedResponse,
  ProblemDetails,
  Todo,
  TodoFilters,
  TodoPriority,
  TodoStatus,
  UpdateTodoRequest,
} from '../models/todo';

const API = '/api/todos';

const EMPTY_PAGE: PagedResponse<Todo> = {
  items: [],
  page: 1,
  pageSize: DEFAULT_FILTERS.pageSize,
  totalCount: 0,
  totalPages: 0,
  hasPreviousPage: false,
  hasNextPage: false,
};

/**
 * Single source of truth for the to-do list. The filter signal drives an
 * `httpResource`, so changing a filter refetches on its own; mutations go through
 * `HttpClient` and then ask that resource to reload.
 */
@Injectable({ providedIn: 'root' })
export class TodoStore {
  private readonly http = inject(HttpClient);

  readonly filters = signal<TodoFilters>({ ...DEFAULT_FILTERS });

  /** Set while a create/update/delete is in flight, so the UI can disable controls. */
  readonly saving = signal(false);

  /** Last mutation failure, already flattened to a readable string. */
  readonly actionError = signal<string | null>(null);

  private readonly page = httpResource<PagedResponse<Todo>>(
    () => ({ url: API, params: toParams(this.filters()) }),
    { defaultValue: EMPTY_PAGE },
  );

  readonly todos = computed(() => this.page.value().items);
  readonly pageInfo = computed(() => this.page.value());
  readonly loading = this.page.isLoading;
  readonly loadError = computed(() =>
    this.page.error() ? 'Could not reach the API. Is it running on port 5062?' : null,
  );

  /** Any filter change resets to page 1 - staying on page 4 of a new filter is never useful. */
  patchFilters(changes: Partial<TodoFilters>): void {
    this.filters.update((current) => ({ ...current, ...changes, page: changes.page ?? 1 }));
  }

  resetFilters(): void {
    this.filters.set({ ...DEFAULT_FILTERS });
  }

  goToPage(page: number): void {
    this.filters.update((current) => ({ ...current, page }));
  }

  create(request: CreateTodoRequest): Promise<boolean> {
    return this.mutate(() => this.http.post<Todo>(API, request));
  }

  update(id: string, request: UpdateTodoRequest): Promise<boolean> {
    return this.mutate(() => this.http.put<Todo>(`${API}/${id}`, request));
  }

  setStatus(id: string, status: TodoStatus): Promise<boolean> {
    return this.mutate(() => this.http.patch<Todo>(`${API}/${id}/status`, { status }));
  }

  setPriority(id: string, priority: TodoPriority): Promise<boolean> {
    return this.mutate(() => this.http.patch<Todo>(`${API}/${id}/priority`, { priority }));
  }

  remove(id: string): Promise<boolean> {
    return this.mutate(() => this.http.delete<void>(`${API}/${id}`));
  }

  private async mutate(request: () => ReturnType<HttpClient['get']>): Promise<boolean> {
    this.saving.set(true);
    this.actionError.set(null);
    try {
      await firstValueFrom(request());
      this.page.reload();
      return true;
    } catch (error) {
      this.actionError.set(describe(error));
      return false;
    } finally {
      this.saving.set(false);
    }
  }
}

/** Drops empty filters so the query string carries only what the user actually chose. */
function toParams(filters: TodoFilters): Record<string, string | number | boolean> {
  const params: Record<string, string | number | boolean> = {
    sortBy: filters.sortBy,
    descending: filters.descending,
    page: filters.page,
    pageSize: filters.pageSize,
  };

  if (filters.status) params['status'] = filters.status;
  if (filters.priority) params['priority'] = filters.priority;
  if (filters.minPriority) params['minPriority'] = filters.minPriority;
  if (filters.search.trim()) params['search'] = filters.search.trim();
  if (filters.overdueOnly) params['isOverdue'] = true;

  return params;
}

/** Turns an API problem-details response into one line a person can act on. */
function describe(error: unknown): string {
  if (!(error instanceof HttpErrorResponse)) {
    return 'Something went wrong.';
  }

  if (error.status === 0) {
    return 'Could not reach the API. Is it running on port 5062?';
  }

  const problem = error.error as ProblemDetails | null;
  const fieldErrors = problem?.errors
    ? Object.entries(problem.errors).map(([field, messages]) => `${field}: ${messages.join(' ')}`)
    : [];

  if (fieldErrors.length) {
    return fieldErrors.join(' | ');
  }

  return problem?.detail ?? problem?.title ?? `Request failed (${error.status}).`;
}
