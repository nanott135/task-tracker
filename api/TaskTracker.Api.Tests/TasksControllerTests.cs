using System.Net;
using System.Net.Http.Json;
using TaskTracker.Api.Dtos;
using Xunit;

namespace TaskTracker.Api.Tests;

public class TasksControllerTests : IDisposable
{
    private readonly ApiWebApplicationFactory _factory = new();
    private readonly HttpClient _client;

    public TasksControllerTests()
    {
        _client = _factory.CreateClient();
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Create_WithMissingTitle_ReturnsBadRequestWithHelpfulMessage()
    {
        var response = await _client.PostAsJsonAsync("/api/tasks", new { description = "no title field" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Title is required.", body);
    }

    [Fact]
    public async Task Create_WithWhitespaceOnlyTitle_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/tasks", new { title = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Title is required.", body);
    }

    [Fact]
    public async Task Create_WithValidTitle_ReturnsCreatedTaskDto()
    {
        var response = await _client.PostAsJsonAsync("/api/tasks", new { title = "Buy milk" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var task = await response.Content.ReadFromJsonAsync<TaskDto>();
        Assert.NotNull(task);
        Assert.Equal("Buy milk", task!.Title);
        Assert.False(task.IsDone);
    }

    [Fact]
    public async Task GetAll_ReturnsEmptyList_WhenNoTasksExist()
    {
        var response = await _client.GetAsync("/api/tasks");

        response.EnsureSuccessStatusCode();
        var tasks = await response.Content.ReadFromJsonAsync<List<TaskDto>>();
        Assert.NotNull(tasks);
        Assert.Empty(tasks!);
    }

    [Fact]
    public async Task GetById_WithUnknownId_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/api/tasks/999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
