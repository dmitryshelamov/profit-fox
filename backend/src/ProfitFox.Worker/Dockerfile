# The Worker image. Build context: backend/, which holds the shared build settings and package versions.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY . .
RUN dotnet publish src/ProfitFox.Worker/ProfitFox.Worker.csproj --configuration Release --output /app

# ASP.NET Core runtime: Infrastructure uses its shared framework.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "ProfitFox.Worker.dll"]
