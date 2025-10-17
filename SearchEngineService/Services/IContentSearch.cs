using Microsoft.EntityFrameworkCore;
using SearchEngineService.Data;
using SearchEngineService.Models;

namespace SearchEngineService.Services;

public interface IContentSearch
{
    IQueryable<Content> Apply(
        AppDbContext db,
        IQueryable<Content> source,
        string query,
        string? type,
        string? sort);
}
