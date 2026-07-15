# Build the Angular app and serve the static bundle with Nginx.
# The API base URL is injected at build time via the ARG below; the Angular
# app talks to the API through the Nginx proxy (see nginx.conf).

# --- Build stage ---
FROM node:20-alpine AS build
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY . .
# API_URL is read by the Angular build (e.g. via src/environments or a
# generated config). Defaults to the docker-compose service name.
ARG API_URL=http://localhost:8080
ENV API_URL=$API_URL
RUN npm run build

# --- Serve stage ---
FROM nginx:alpine AS runtime
COPY --from=build /app/dist/customer-service-dashboard/browser /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]
