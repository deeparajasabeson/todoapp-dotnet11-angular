import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { PagedResponse, Todo } from '../models/todo';
import { TodoStore } from '../services/todo-store';
import { TodoList } from './todo-list';

function page(items: Partial<Todo>[]): PagedResponse<Todo> {
  const full = items.map((item, index) => ({
    id: `id-${index}`,
    title: `Item ${index}`,
    status: 'Pending',
    priority: 'Medium',
    isOverdue: false,
    createdAt: '2026-09-01T00:00:00Z',
    updatedAt: '2026-09-01T00:00:00Z',
    ...item,
  })) as Todo[];

  return {
    items: full,
    page: 1,
    pageSize: 10,
    totalCount: full.length,
    totalPages: 1,
    hasPreviousPage: false,
    hasNextPage: false,
  };
}

describe('TodoList', () => {
  let fixture: ComponentFixture<TodoList>;
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TodoList],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    }).compileComponents();

    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(TodoList);
  });

  afterEach(() => http.verify());

  /**
   * Lets pending work drain, then runs change detection. `fixture.whenStable()` does
   * not settle here - the resource keeps the fixture busy - so drive it explicitly.
   */
  async function settle() {
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
  }

  /** Flushes the pending list request with the given items and settles the view. */
  async function load(items: Partial<Todo>[]) {
    fixture.detectChanges();
    const request = http.expectOne((r) => r.url === '/api/todos');
    request.flush(page(items));
    await settle();
    return request;
  }

  it('sends the default filters as query parameters', async () => {
    const request = await load([]);

    expect(request.request.params.get('sortBy')).toBe('Priority');
    expect(request.request.params.get('descending')).toBe('true');
    expect(request.request.params.get('page')).toBe('1');
    // Unset filters must not appear at all - the API treats absent as "no filter".
    expect(request.request.params.has('status')).toBe(false);
    expect(request.request.params.has('search')).toBe(false);
  });

  it('renders a row per item with its status and priority', async () => {
    await load([
      {
        title: 'Renew domain',
        priority: 'Critical',
        status: 'Pending',
        dueDate: '2026-09-01T00:00:00Z',
        isOverdue: true,
      },
      { title: 'Buy milk', priority: 'Low', status: 'Completed' },
    ]);

    const host = fixture.nativeElement as HTMLElement;
    const rows = host.querySelectorAll('li.todo');
    expect(rows.length).toBe(2);
    expect(host.textContent).toContain('Renew domain');
    expect(host.querySelector('.badge.priority-critical')?.textContent?.trim()).toBe('Critical');
    expect(host.querySelector('.due.is-overdue')).toBeTruthy();
    expect(rows[1].classList.contains('done')).toBe(true);
  });

  it('refetches with the status filter when it changes', async () => {
    await load([]);

    TestBed.inject(TodoStore).patchFilters({ status: 'InProgress' });
    await settle();

    const request = http.expectOne((r) => r.url === '/api/todos');
    expect(request.request.params.get('status')).toBe('InProgress');
    request.flush(page([]));
    await settle();
  });

  it('shows an empty state when nothing matches', async () => {
    await load([]);

    expect((fixture.nativeElement as HTMLElement).querySelector('li.empty')).toBeTruthy();
  });
});
