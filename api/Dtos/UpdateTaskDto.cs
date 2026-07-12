using System.ComponentModel.DataAnnotations;

namespace TaskTracker.Api.Dtos;

public class UpdateTaskDto
{
    [Required]
    public required string Title { get; set; }
    public string? Description { get; set; }
    public bool IsDone { get; set; }
    public DateTime? DueDate { get; set; }
}
