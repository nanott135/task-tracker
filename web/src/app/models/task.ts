export interface Task {
  id: number;
  title: string;
  description: string | null;
  isDone: boolean;
  dueDate: string | null;
  createdAt: string;
}

export interface CreateTask {
  title: string;
  description: string | null;
  isDone: boolean;
  dueDate: string | null;
}

export type UpdateTask = CreateTask;
