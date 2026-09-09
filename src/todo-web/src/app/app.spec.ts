import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';
import { routes } from './app.routes';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter(routes),
      ],
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('renders the heading and both panes', async () => {
    const fixture = TestBed.createComponent(App);
    // The shell hosts the chat panel, whose httpResource keeps whenStable() from settling.
    fixture.detectChanges();

    const http = TestBed.inject(HttpTestingController);
    http.match(() => true).forEach((request) => request.flush({ available: false }));

    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('h1')?.textContent).toContain('Todos');
    expect(compiled.querySelector('.list-pane')).toBeTruthy();
    expect(compiled.querySelector('.chat-pane')).toBeTruthy();
  });
});
