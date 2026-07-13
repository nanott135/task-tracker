using System.ComponentModel.DataAnnotations;

namespace TaskTracker.Api.Dtos;

public class CreateTaskDto
{
    [Required(ErrorMessage = "Title is required.")]
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsDone { get; set; }
    public DateTime? DueDate { get; set; }
}
