namespace JobForge.Domain.Exceptions;

/// <summary>Base type for all application-recognized (non-infrastructure) failures.</summary>
public abstract class JobForgeException : Exception
{
    protected JobForgeException(string message) : base(message) { }
}

/// <summary>Requested entity does not exist, or does not belong to the caller (never leaks which).</summary>
public sealed class NotFoundException : JobForgeException
{
    public NotFoundException(string entity, object id) : base($"{entity} '{id}' was not found.") { }
}

/// <summary>The request conflicts with current state (e.g. a Run Now while an execution is already active).</summary>
public sealed class ConflictException : JobForgeException
{
    public ConflictException(string message) : base(message) { }
}

/// <summary>Request payload failed domain validation rules.</summary>
public sealed class ValidationException : JobForgeException
{
    public ValidationException(string message) : base(message) { }
}

/// <summary>Credentials were missing or invalid.</summary>
public sealed class UnauthorizedException : JobForgeException
{
    public UnauthorizedException(string message) : base(message) { }
}

/// <summary>An execution state transition was attempted that the state machine does not allow.</summary>
public sealed class InvalidStateTransitionException : JobForgeException
{
    public InvalidStateTransitionException(Enums.ExecutionStatus from, Enums.ExecutionStatus to)
        : base($"Cannot transition execution from {from} to {to}.") { }
}
