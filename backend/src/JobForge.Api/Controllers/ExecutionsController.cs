using JobForge.Api.Extensions;
using JobForge.Application.DTOs;
using JobForge.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobForge.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/executions")]
public sealed class ExecutionsController : ControllerBase
{
    private readonly IExecutionService _executionService;

    public ExecutionsController(IExecutionService executionService)
    {
        _executionService = executionService;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ExecutionDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await _executionService.GetByIdAsync(User.GetUserId(), id, cancellationToken));
    }

    /// <summary>Creates a brand-new execution referencing the failed one; the original row is left untouched.</summary>
    [HttpPost("{id:guid}/retry")]
    public async Task<ActionResult<ExecutionDto>> Retry(Guid id, CancellationToken cancellationToken)
    {
        var execution = await _executionService.RetryAsync(User.GetUserId(), id, cancellationToken);
        return Ok(execution);
    }
}
