import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { TaskService } from './task.service';
import { Task } from '../models/task';

describe('TaskService', () => {
  let service: TaskService;
  let httpMock: HttpTestingController;

  const sampleTask: Task = {
    id: 1,
    title: 'Buy milk',
    description: null,
    isDone: false,
    dueDate: null,
    createdAt: '2026-07-13T00:00:00Z',
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), TaskService],
    });
    service = TestBed.inject(TaskService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('getAll() sends a GET to /api/tasks and returns the list', () => {
    let result: Task[] | undefined;
    service.getAll().subscribe((tasks) => (result = tasks));

    const req = httpMock.expectOne('/api/tasks');
    expect(req.request.method).toBe('GET');
    req.flush([sampleTask]);

    expect(result).toEqual([sampleTask]);
  });

  it('create() sends a POST to /api/tasks with the task body', () => {
    const createBody = { title: 'Buy milk', description: null, isDone: false, dueDate: null };
    let result: Task | undefined;
    service.create(createBody).subscribe((task) => (result = task));

    const req = httpMock.expectOne('/api/tasks');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(createBody);
    req.flush(sampleTask);

    expect(result).toEqual(sampleTask);
  });

  it('update() sends a PUT to /api/tasks/:id with the task body', () => {
    const updateBody = { title: 'Buy oat milk', description: null, isDone: true, dueDate: null };
    let completed = false;
    service.update(1, updateBody).subscribe(() => (completed = true));

    const req = httpMock.expectOne('/api/tasks/1');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(updateBody);
    req.flush(null);

    expect(completed).toBe(true);
  });

  it('delete() sends a DELETE to /api/tasks/:id', () => {
    let completed = false;
    service.delete(1).subscribe(() => (completed = true));

    const req = httpMock.expectOne('/api/tasks/1');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);

    expect(completed).toBe(true);
  });
});
