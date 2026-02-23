# ── Stage 1: Restore ──
FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS restore
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props DataverseForge.slnx ./
COPY src/DataverseForge.Domain/DataverseForge.Domain.csproj src/DataverseForge.Domain/
COPY src/DataverseForge.Application/DataverseForge.Application.csproj src/DataverseForge.Application/
COPY src/DataverseForge.Infrastructure/DataverseForge.Infrastructure.csproj src/DataverseForge.Infrastructure/
COPY src/DataverseForge.Api/DataverseForge.Api.csproj src/DataverseForge.Api/

RUN dotnet restore DataverseForge.slnx --runtime linux-x64

# ── Stage 2: Build ──
FROM restore AS build
COPY src/ src/
RUN dotnet build DataverseForge.slnx -c Release --no-restore

# ── Stage 3: Publish ──
FROM build AS publish
RUN dotnet publish src/DataverseForge.Api/DataverseForge.Api.csproj -c Release --no-build -o /app/publish

# ── Stage 4: Runtime ──
FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS runtime
WORKDIR /app

RUN mkdir -p /app/data

COPY --from=publish /app/publish .

ENV ASPNETCORE_URLS=http://+:5000
EXPOSE 5000

ENTRYPOINT ["dotnet", "DataverseForge.Api.dll"]
