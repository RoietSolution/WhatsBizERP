FROM node:22-alpine AS build
WORKDIR /src
COPY frontend/WhatsBiz.Web/package*.json ./
RUN npm ci
COPY frontend/WhatsBiz.Web/ ./
RUN npm run build
FROM nginx:1.27-alpine
COPY --from=build /src/dist/whats-biz.web/browser /usr/share/nginx/html
EXPOSE 80
