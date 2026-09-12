using JobForge.Application.Common;
using JobForge.Application.DTOs;

namespace JobForge.Application.Interfaces;

public interface IJobService
{
    Task<PagedResult<JobDto>> ListAsync(Guid userId, string? search, int page, int pageSize, CancellationToken cancellationToken);
    Task<JobDto> GetByIdAsync(Guid userId, Guid jobId, CancellationToken cancellationToken);
    Task<JobDto> CreateAsync(Guid userId, CreateJobRequest request, CancellationToken cancellationToken);
    Task<JobDto> UpdateAsync(Guid userId, Guid jobId, UpdateJobRequest request, CancellationToken cancellationToken);
    Task DeleteAsync(Guid userId, Guid jobId, CancellationToken cancellationToken);
    Task<JobDto> PauseAsync(Guid userId, Guid jobId, CancellationToken cancellationToken);
    Task<JobDto> ResumeAsync(Guid userId, Guid jobId, CancellationToken cancellationToken);
}
