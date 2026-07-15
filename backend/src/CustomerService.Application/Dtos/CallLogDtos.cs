using System.ComponentModel.DataAnnotations;
using CustomerService.Domain.Entities;

namespace CustomerService.Application.Dtos;

/// <summary>Data transfer object for creating a call / follow-up log.</summary>
public class CreateCallLogDto
{
    /// <summary>Parent case id.</summary>
    [Range(1, int.MaxValue, ErrorMessage = "A valid case is required.")]
    public int CaseId { get; set; }

    /// <summary>Direction of contact.</summary>
    [Required(ErrorMessage = "Direction is required.")]
    public CallDirection Direction { get; set; } = CallDirection.Outbound;

    /// <summary>Notes.</summary>
    [Required(ErrorMessage = "Notes are required.")]
    public string Notes { get; set; } = string.Empty;

    /// <summary>Call duration in seconds.</summary>
    [Range(0, int.MaxValue, ErrorMessage = "Duration cannot be negative.")]
    public int DurationSeconds { get; set; }
}

/// <summary>Read model for a call log.</summary>
public class CallLogDto
{
    /// <summary>Log primary key.</summary>
    public int Id { get; set; }

    /// <summary>Parent case id.</summary>
    public int CaseId { get; set; }

    /// <summary>Direction.</summary>
    public CallDirection Direction { get; set; }

    /// <summary>Notes.</summary>
    public string Notes { get; set; } = string.Empty;

    /// <summary>Duration seconds.</summary>
    public int DurationSeconds { get; set; }

    /// <summary>Agent who logged it.</summary>
    public string? LoggedByUserId { get; set; }

    /// <summary>Created timestamp (UTC).</summary>
    public DateTime CreatedAtUtc { get; set; }
}
