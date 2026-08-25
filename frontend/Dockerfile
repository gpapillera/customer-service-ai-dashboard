# Build the Angular app and serve the static bundle with Nginx.
# The SPA calls the API via relative `/api/...` URLs (see auth.service.ts and the
# shared services), so no API host is baked in at build time — Nginx proxies
# `/api/` to the backend service (see nginx.conf). One image works regardless of
# the API's address on the docker network.

# --- Build stage ---
FROM node:20-alpine AS build
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY . .
RUN npm run build

# --- Serve stage ---
FROM nginx:alpine AS runtime
COPY --from=build /app/dist/customer-service-dashboard/browser /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]
