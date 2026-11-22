using CyberHub.ServerService.Data;
using CyberHub.ServerService.Services;
using CyberHub.Shared.Models;
using CyberHub.Shared.Security;
using Microsoft.AspNetCore.Mvc;

namespace CyberHub.ServerService.Controllers;

[ApiController]
[Route("api/admin/categories")]
public sealed class AdminCategoriesController : ControllerBase
{
    private readonly SqliteGameRepository _repository;
    private readonly CatalogSyncService _syncService;

    public AdminCategoriesController(SqliteGameRepository repository, CatalogSyncService syncService)
    {
        _repository = repository;
        _syncService = syncService;
    }

    [HttpGet]
    public async Task<ActionResult<List<CategoryEntry>>> GetCategories(CancellationToken ct)
    {
        return Ok(await _repository.GetCategoriesAsync(ct));
    }

    [HttpPost]
    public async Task<ActionResult<CategoryEntry>> CreateCategory([FromBody] UpsertCategoryRequest request, CancellationToken ct)
    {
        (bool valid, string error) = UncPathValidator.ValidateCategory(request);
        if (!valid)
        {
            return BadRequest(new { error });
        }

        var category = new CategoryEntry
        {
            Id = "cat-" + Guid.NewGuid().ToString("N")[..8],
            Slug = UncPathValidator.GenerateSafeSlug(request.NameEn),
            NameEn = request.NameEn.Trim(),
            NameAr = request.NameAr.Trim(),
            IconKey = string.IsNullOrWhiteSpace(request.IconKey) ? "gamepad" : request.IconKey.Trim(),
            BannerImagePath = request.BannerImagePath.Trim(),
            DisplayOrder = request.DisplayOrder,
            IsSystem = false,
            FilterType = CategoryFilterType.Custom,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        await _repository.UpsertCategoryAsync(category, isNew: true, ct);
        await _syncService.PublishSnapshotToShareAsync(ct);

        await AppendLogAsync("CATEGORY_CREATED", category.Id, category.NameEn, $"Created category '{category.NameEn}' / '{category.NameAr}'", ct);
        return CreatedAtAction(nameof(GetCategories), new { id = category.Id }, category);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<CategoryEntry>> UpdateCategory(string id, [FromBody] UpsertCategoryRequest request, CancellationToken ct)
    {
        CategoryEntry? existing = await _repository.GetCategoryByIdAsync(id, ct);
        if (existing is null)
        {
            return NotFound(new { error = "Category not found." });
        }

        (bool valid, string error) = UncPathValidator.ValidateCategory(request);
        if (!valid)
        {
            return BadRequest(new { error });
        }

        existing.NameEn = request.NameEn.Trim();
        existing.NameAr = request.NameAr.Trim();
        existing.IconKey = string.IsNullOrWhiteSpace(request.IconKey) ? existing.IconKey : request.IconKey.Trim();
        existing.BannerImagePath = request.BannerImagePath.Trim();
        existing.DisplayOrder = request.DisplayOrder;
        existing.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await _repository.UpsertCategoryAsync(existing, isNew: false, ct);
        await _syncService.PublishSnapshotToShareAsync(ct);

        await AppendLogAsync("CATEGORY_UPDATED", existing.Id, existing.NameEn, $"Updated category '{existing.NameEn}' / '{existing.NameAr}' (Order={existing.DisplayOrder})", ct);
        return Ok(existing);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteCategory(string id, CancellationToken ct)
    {
        CategoryEntry? existing = await _repository.GetCategoryByIdAsync(id, ct);
        if (existing is null)
        {
            return NotFound(new { error = "Category not found." });
        }

        (bool deleted, string error) = await _repository.DeleteCategorySafelyAsync(id, ct);
        if (!deleted)
        {
            return BadRequest(new { error });
        }

        await _syncService.PublishSnapshotToShareAsync(ct);
        await AppendLogAsync("CATEGORY_DELETED", id, existing.NameEn, $"Safely removed category '{existing.NameEn}' without touching any game records or disk files.", ct);
        return NoContent();
    }

    private Task AppendLogAsync(string action, string targetId, string targetName, string details, CancellationToken ct)
    {
        string workstation = HttpContext.Items["AdminWorkstation"] as string ?? "ADMIN-PC";
        string ip = HttpContext.Items["AdminSourceIp"] as string ?? "127.0.0.1";
        return _repository.AppendAuditLogAsync(new AdminAuditLog
        {
            Action = action,
            TargetGameId = targetId,
            TargetGameName = targetName,
            WorkstationName = workstation,
            SourceIp = ip,
            Details = details
        }, ct);
    }
}
