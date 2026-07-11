namespace CustomerService.Domain.Entities;

/// <summary>
/// A support category used to classify cases (e.g. Billing, Shipping, Technical).
/// </summary>
public class Category
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Category name (unique).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional longer description.</summary>
    public string? Description { get; set; }

    /// <summary>Navigation property: cases in this category.</summary>
    public ICollection<Case> Cases { get; set; } = new List<Case>();
}
