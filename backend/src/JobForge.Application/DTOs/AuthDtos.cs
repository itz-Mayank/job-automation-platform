using System.ComponentModel.DataAnnotations;

namespace JobForge.Application.DTOs;

public sealed class RegisterRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; set; } = default!;

    [Required, MinLength(8), MaxLength(128)]
    public string Password { get; set; } = default!;
}

public sealed class LoginRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; set; } = default!;

    [Required]
    public string Password { get; set; } = default!;
}

public sealed record UserDto(Guid Id, string Email, DateTimeOffset CreatedAt);

public sealed record AuthResponse(string Token, UserDto User);
