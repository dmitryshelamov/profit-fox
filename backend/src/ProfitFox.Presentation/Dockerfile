# The API image. Build context: backend/, which holds the shared build settings and package versions.
# It listens on 8080, the default port of the ASP.NET Core image, where the web image's Caddy forwards /api.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY . .
RUN dotnet publish src/ProfitFox.Presentation/ProfitFox.Presentation.csproj --configuration Release --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "ProfitFox.Presentation.dll"]
