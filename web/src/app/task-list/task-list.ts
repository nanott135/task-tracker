import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';

import { TaskService } from '../services/task.service';
import { Task } from '../models/task';

@Component({
  selector: 'app-task-list',
  imports: [FormsModule, DatePipe],
  templateUrl: './task-list.html',
  styleUrl: './task-list.css',
})
export class TaskList implements OnInit {
  private readonly taskService = inject(TaskService);

  readonly tasks = signal<Task[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly titleError = signal<string | null>(null);

  newTitle = '';
  newDescription = '';
  newDueDate = '';

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.taskService.getAll().subscribe({
      next: (tasks) => {
        this.tasks.set(tasks);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Could not load tasks. Is the API running?');
        this.loading.set(false);
      },
    });
  }

  addTask(): void {
    const title = this.newTitle.trim();
    if (!title) {
      this.titleError.set('Title is required.');
      return;
    }
    this.titleError.set(null);

    this.taskService
      .create({
        title,
        description: this.newDescription.trim() || null,
        isDone: false,
        dueDate: this.newDueDate || null,
      })
      .subscribe({
        next: (task) => {
          this.tasks.update((tasks) => [...tasks, task]);
          this.newTitle = '';
          this.newDescription = '';
          this.newDueDate = '';
        },
        error: () => this.error.set('Could not add the task. Please try again.'),
      });
  }

  onTitleInput(): void {
    if (this.titleError() && this.newTitle.trim()) {
      this.titleError.set(null);
    }
  }

  toggleDone(task: Task): void {
    const updated = { ...task, isDone: !task.isDone };
    this.tasks.update((tasks) => tasks.map((t) => (t.id === task.id ? updated : t)));

    this.taskService
      .update(task.id, {
        title: updated.title,
        description: updated.description,
        isDone: updated.isDone,
        dueDate: updated.dueDate,
      })
      .subscribe({
        error: () => {
          this.tasks.update((tasks) => tasks.map((t) => (t.id === task.id ? task : t)));
          this.error.set('Could not update the task. Please try again.');
        },
      });
  }

  removeTask(task: Task): void {
    this.taskService.delete(task.id).subscribe({
      next: () => this.tasks.update((tasks) => tasks.filter((t) => t.id !== task.id)),
      error: () => this.error.set('Could not delete the task. Please try again.'),
    });
  }
}
