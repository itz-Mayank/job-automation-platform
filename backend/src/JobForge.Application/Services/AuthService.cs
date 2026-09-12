using JobForge.Application.Common;
using JobForge.Application.DTOs;
using JobForge.Application.Interfaces;
using JobForge.Domain.Entities;
using JobForge.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace JobForge.Application.Services;

public sealed class AuthService : IAuthService
{
    private readonly IApplicationDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;

    public AuthService(IApplicationDbContext db, IPasswordHasher passwordHasher, IJwtTokenGenerator jwtTokenGenerator)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _jwtTokenGenerator = jwtTokenGenerator;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var exists = await _db.Users.AnyAsync(u => u.Email == normalizedEmail, cancellationToken);
        if (exists)
        {
            throw new ConflictException("An account with this email already exists.");
        }

        var user = User.Create(normalizedEmail, _passwordHasher.Hash(request.Password));
        _db.Users.Add(user);
        await _db.SaveChangesAsync(cancellationToken);

        return ToAuthResponse(user);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);
        if (user is null || !_passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            throw new UnauthorizedException("Invalid email or password.");
        }

        return ToAuthResponse(user);
    }

    public async Task<UserDto> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _db.Users.FindAsync(new object[] { userId }, cancellationToken)
            ?? throw new NotFoundException(nameof(User), userId);

        return new UserDto(user.Id, user.Email, user.CreatedAt);
    }

    private AuthResponse ToAuthResponse(User user) =>
        new(_jwtTokenGenerator.GenerateToken(user), new UserDto(user.Id, user.Email, user.CreatedAt));
}
