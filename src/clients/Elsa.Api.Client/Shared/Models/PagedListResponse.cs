namespace Elsa.Api.Client.Shared.Models;

/// <summary>
/// Represents a generic paged list response that offers a unified format for returning paged list of things from API endpoints.
/// </summary>
/// <param name="Items">A page of items.</param>
/// <param name="TotalCount">The total number of items.</param>
/// <typeparam name="T">The type of the items.</typeparam>
public class PagedListResponse<T> : LinkedEntity
{
    public ICollection<T> Items { get; set; }
    public long TotalCount { get; set; }
}