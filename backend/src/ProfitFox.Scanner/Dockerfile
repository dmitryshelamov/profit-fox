# The scanner image. Build context: backend/, which holds the shared build settings and package versions.
# On the server it runs as one more service of the production compose file, next to the API and the Worker:
#   scanner:
#     build: { context: backend, dockerfile: src/ProfitFox.Scanner/Dockerfile }
#     restart: unless-stopped
#     environment:
#       DOTNET_ENVIRONMENT: Production
#       ConnectionStrings__MarketData: <the same connection string as the API's>
# It needs no master key and no port: it reads only public Bybit data and writes the market tables the API migrated.
# One scanner holds the stream at a time; a second one only waits for the lease. The first load of history takes hours.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY . .
RUN dotnet publish src/ProfitFox.Scanner/ProfitFox.Scanner.csproj --configuration Release --output /app

# ASP.NET Core runtime: Infrastructure uses its shared framework.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "ProfitFox.Scanner.dll"]
