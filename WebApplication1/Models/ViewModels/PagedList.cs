using Microsoft.EntityFrameworkCore;

namespace WebApplication1.Models.ViewModels;

// Paging metadata only, kept separate from the page's items so views can bind
// a plain @model List<T>-shaped PagedList<T> while still rendering a pager
// via the shared _Pager partial (which takes just this record as its model).
public record PageInfo(int PageNumber, int TotalPages, int TotalCount, int PageSize)
{
    public bool HasPrevious => PageNumber > 1;
    public bool HasNext => PageNumber < TotalPages;
}

public class PagedList<T> : List<T>
{
    public const int DefaultPageSize = 20;

    public PageInfo Paging { get; }

    public PagedList(List<T> items, int totalCount, int pageNumber, int pageSize)
    {
        Paging = new PageInfo(pageNumber, (int)Math.Ceiling(totalCount / (double)pageSize), totalCount, pageSize);
        AddRange(items);
    }

    public static async Task<PagedList<T>> CreateAsync(IQueryable<T> source, int pageNumber, int pageSize = DefaultPageSize)
    {
        pageNumber = Math.Max(1, pageNumber);
        var totalCount = await source.CountAsync();
        var items = await source.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync();
        return new PagedList<T>(items, totalCount, pageNumber, pageSize);
    }

    // For lists already materialized in memory (service methods that return List<T> rather than IQueryable<T>).
    public static PagedList<T> Create(List<T> source, int pageNumber, int pageSize = DefaultPageSize)
    {
        pageNumber = Math.Max(1, pageNumber);
        var items = source.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList();
        return new PagedList<T>(items, source.Count, pageNumber, pageSize);
    }
}
