# The image of the bench Worker, which counts the runs of the queue in the database. Build context: the repository root.
# It has no port; the name of the Worker comes from Worker__Name.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY backend/ backend/
RUN dotnet publish backend/src/ProfitFox.Worker/ProfitFox.Worker.csproj --configuration Release --output /app

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .
# The folder of the cache of history (MarketData__HistoryCache__Path in docker-compose.prod.yml), owned by the user of the
# image: an empty named volume mounted on it takes its owner on the first mount, so the process can write there.
RUN mkdir --parents /var/cache/profit-fox/history && chown "$APP_UID" /var/cache/profit-fox/history
USER $APP_UID
ENTRYPOINT ["dotnet", "ProfitFox.Worker.dll"]
