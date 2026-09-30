# Build context: clients/web
FROM node:24-alpine AS build
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci

COPY . .
# Builds every language (dist/web/browser/en and dist/web/browser/es).
RUN npx ng build

FROM nginx:1.29-alpine AS runtime
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/web/browser /usr/share/nginx/html
EXPOSE 80
