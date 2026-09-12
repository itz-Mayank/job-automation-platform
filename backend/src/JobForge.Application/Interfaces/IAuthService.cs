using JobForge.Application.DTOs;

namespace JobForge.Application.Interfaces;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken);
    Task<UserDto> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken);
}
