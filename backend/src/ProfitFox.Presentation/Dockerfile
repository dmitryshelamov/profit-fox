# The image of the bench API with the page of the bench. Build context: the repository root, because the image needs
# frontend/ besides backend/. It listens where ASPNETCORE_URLS or the Kestrel section says; docker-compose.prod.yml
# opens it on 5240.
FROM node:22 AS frontend
WORKDIR /source/frontend
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY backend/ backend/
RUN dotnet publish backend/src/ProfitFox.Presentation/ProfitFox.Presentation.csproj --configuration Release --output /app
# The service serves the folder wwwroot with the fallback to index.html when it exists.
COPY --from=frontend /source/frontend/dist /app/wwwroot

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# The healthcheck of docker-compose.prod.yml asks /health with curl, which the image lacks.
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "ProfitFox.Presentation.dll"]
