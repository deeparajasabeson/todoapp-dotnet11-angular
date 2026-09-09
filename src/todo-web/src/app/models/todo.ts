/** Mirrors the enums in the API. Values travel as names, not numbers. */
export const TODO_STATUSES = ['Pending', 'InProgress', 'Completed', 'Cancelled'] as const;
export type TodoStatus = (typeof TODO_STATUSES)[number];

/** Ordered least to most urgent, matching the API's underlying enum values. */
export const TODO_PRIORITIES = ['Low', 'Medium', 'High', 'Critical'] as const;
export type TodoPriority = (typeof TODO_PRIORITIES)[number];

export const TODO_SORT_FIELDS = [
  'CreatedAt',
  'UpdatedAt',
  'DueDate',
  'Priority',
  'Status',
  'Title',
] as const;
export type TodoSortBy = (typeof TODO_SORT_FIELDS)[number];

export interface Todo {
  readonly id: string;
  readonly title: string;
  readonly description?: string;
  readonly status: TodoStatus;
  readonly priority: TodoPriority;
  readonly dueDate?: string;
  readonly isOverdue: boolean;
  readonly createdAt: string;
  readonly updatedAt: string;
  readonly completedAt?: string;
}

export interface PagedResponse<T> {
  readonly items: readonly T[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
  readonly totalPages: number;
  readonly hasPreviousPage: boolean;
  readonly hasNextPage: boolean;
}

/** Everything `GET /api/todos` accepts. Empty values are dropped before the request. */
export interface TodoFilters {
  status: TodoStatus | '';
  priority: TodoPriority | '';
  minPriority: TodoPriority | '';
  search: string;
  overdueOnly: boolean;
  sortBy: TodoSortBy;
  descending: boolean;
  page: number;
  pageSize: number;
}

export const DEFAULT_FILTERS: TodoFilters = {
  status: '',
  priority: '',
  minPriority: '',
  search: '',
  overdueOnly: false,
  sortBy: 'Priority',
  descending: true,
  page: 1,
  pageSize: 10,
};

export interface CreateTodoRequest {
  title: string;
  description?: string | null;
  status?: TodoStatus;
  priority?: TodoPriority;
  dueDate?: string | null;
}

export interface UpdateTodoRequest {
  title: string;
  description?: string | null;
  status: TodoStatus;
  priority: TodoPriority;
  dueDate?: string | null;
}

/** The shape the API returns for a 400: RFC 9457 problem details. */
export interface ProblemDetails {
  title?: string;
  detail?: string;
  status?: number;
  errors?: Record<string, string[]>;
}
