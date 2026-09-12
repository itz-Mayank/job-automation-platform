using JobForge.Domain.Entities;

namespace JobForge.Application.Interfaces;

public interface IJwtTokenGenerator
{
    string GenerateToken(User user);
}
