# The image of the collector of the trade flow. Build context: backend/, which holds the shared build settings and package versions.
# On the server it runs as one more service of the production compose file, next to the API, the Worker and the scanner, limited to one core:
#   trade-flow:
#     image: <registry>/trade-flow:<version>
#     restart: unless-stopped
#     cpus: 1
#     mem_limit: 512m
#     environment:
#       DOTNET_ENVIRONMENT: Production
#       ConnectionStrings__MarketData: <the same connection string as the API's>
# It needs no master key and no port: it reads only public Bybit data (the stream of trades and the daily archive) and writes the tables of
# the trade flow the API migrated. One collector holds the flow at a time; a second one only waits for the lease.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY . .
RUN dotnet publish src/ProfitFox.TradeFlow/ProfitFox.TradeFlow.csproj --configuration Release --output /app

# ASP.NET Core runtime: Infrastructure uses its shared framework.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "ProfitFox.TradeFlow.dll"]
