import { Routes } from '@angular/router';
import { TodoList } from './todos/todo-list';

export const routes: Routes = [
  { path: '', component: TodoList, title: 'Todos' },
  { path: '**', redirectTo: '' },
];
