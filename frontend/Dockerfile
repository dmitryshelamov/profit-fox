# The web image: the built frontend served by Caddy, which forwards /api to the API. One address for the interface and
# the API, so the API needs no CORS. HTTP only for now; HTTPS comes with authentication (design of add-strategy-editor).
# Build from the frontend folder: docker build -t profit-fox-web frontend
FROM node:24-alpine AS build
WORKDIR /src
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
# Only the bundle: tests read backend fixtures outside this context, and CI typechecks them.
RUN npx vite build

FROM caddy:2.11-alpine
COPY Caddyfile /etc/caddy/Caddyfile
COPY --from=build /src/dist /srv
