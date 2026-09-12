using JobForge.Api.Extensions;
using JobForge.Application.DTOs;
using JobForge.Application.Interfaces;
using JobForge.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobForge.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/jobs")]
public sealed class JobsController : ControllerBase
{
    private readonly IJobService _jobService;
    private readonly IExecutionService _executionService;

    public JobsController(IJobService jobService, IExecutionService executionService)
    {
        _jobService = jobService;
        _executionService = executionService;
    }

    [HttpGet]
    public async Task<ActionResult> List(
        [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var result = await _jobService.ListAsync(User.GetUserId(), search, page, pageSize, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<JobDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await _jobService.GetByIdAsync(User.GetUserId(), id, cancellationToken));
    }

    [HttpPost]
    public async Task<ActionResult<JobDto>> Create(CreateJobRequest request, CancellationToken cancellationToken)
    {
        var job = await _jobService.CreateAsync(User.GetUserId(), request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = job.Id }, job);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<JobDto>> Update(Guid id, UpdateJobRequest request, CancellationToken cancellationToken)
    {
        return Ok(await _jobService.UpdateAsync(User.GetUserId(), id, request, cancellationToken));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _jobService.DeleteAsync(User.GetUserId(), id, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/pause")]
    public async Task<ActionResult<JobDto>> Pause(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await _jobService.PauseAsync(User.GetUserId(), id, cancellationToken));
    }

    [HttpPost("{id:guid}/resume")]
    public async Task<ActionResult<JobDto>> Resume(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await _jobService.ResumeAsync(User.GetUserId(), id, cancellationToken));
    }

    /// <summary>
    /// Triggers a manual execution. Safe to click repeatedly or double-submit: if an
    /// execution is already active for this job, the existing one is returned instead
    /// of creating a duplicate. Pass an Idempotency-Key header to make retried network
    /// requests (not user clicks) return the exact same execution they originally created.
    /// </summary>
    [HttpPost("{id:guid}/run")]
    public async Task<ActionResult<ExecutionDto>> RunNow(Guid id, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken cancellationToken)
    {
        var execution = await _executionService.RunNowAsync(User.GetUserId(), id, idempotencyKey, cancellationToken);
        return Ok(execution);
    }

    [HttpGet("{id:guid}/executions")]
    public async Task<ActionResult> ListExecutions(
        Guid id, [FromQuery] ExecutionStatus? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var result = await _executionService.ListForJobAsync(User.GetUserId(), id, status, page, pageSize, cancellationToken);
        return Ok(result);
    }
}
