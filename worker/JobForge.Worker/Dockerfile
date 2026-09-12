FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY backend/JobForge.sln ./backend/
COPY backend/src/JobForge.Domain/JobForge.Domain.csproj backend/src/JobForge.Domain/
COPY backend/src/JobForge.Application/JobForge.Application.csproj backend/src/JobForge.Application/
COPY backend/src/JobForge.Infrastructure/JobForge.Infrastructure.csproj backend/src/JobForge.Infrastructure/
COPY backend/src/JobForge.Api/JobForge.Api.csproj backend/src/JobForge.Api/
COPY backend/tests/JobForge.Tests/JobForge.Tests.csproj backend/tests/JobForge.Tests/
COPY worker/JobForge.Worker/JobForge.Worker.csproj worker/JobForge.Worker/
RUN dotnet restore worker/JobForge.Worker/JobForge.Worker.csproj

COPY backend/src backend/src
COPY worker/JobForge.Worker worker/JobForge.Worker
RUN dotnet publish worker/JobForge.Worker/JobForge.Worker.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
RUN useradd --uid 5678 --user-group --create-home appuser
COPY --from=build /app .
USER appuser
ENTRYPOINT ["dotnet", "JobForge.Worker.dll"]
