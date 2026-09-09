import { Component, signal } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ChatPanel } from './chat/chat-panel';

@Component({
  imports: [RouterOutlet, ChatPanel],
  selector: 'app-root',
  styleUrl: './app.css',
  templateUrl: './app.html',
})
export class App {
  protected readonly title = signal('todo-web');
}
