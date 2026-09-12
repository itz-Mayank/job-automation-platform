using JobForge.Application.DTOs;

namespace JobForge.Application.Interfaces;

public interface IDashboardService
{
    Task<DashboardSummaryDto> GetSummaryAsync(Guid userId, CancellationToken cancellationToken);
}
