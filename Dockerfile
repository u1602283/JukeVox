# Stage 1 — Build frontend
FROM node:22-alpine AS frontend-build
WORKDIR /app/src/Jukevox.Client
COPY src/Jukevox.Client/package.json src/Jukevox.Client/package-lock.json ./
RUN npm ci
COPY src/Jukevox.Client/ ./
RUN npm run build

# Stage 2 — Build backend
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS backend-build
WORKDIR /app
COPY src/Jukevox.Server/Jukevox.Server.csproj src/Jukevox.Server/
RUN dotnet restore src/Jukevox.Server/Jukevox.Server.csproj
COPY src/ src/
COPY --from=frontend-build /app/src/Jukevox.Server/wwwroot/ src/Jukevox.Server/wwwroot/
RUN dotnet publish src/Jukevox.Server/Jukevox.Server.csproj -c Release -o /app/publish --no-restore

# Stage 3 — Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
WORKDIR /app
COPY --from=backend-build /app/publish .
EXPOSE 8080
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true
ENV ASPNETCORE_URLS=http://+:8080
RUN addgroup -g 10000 -S jukevox && adduser -u 10000 -S jukevox -G jukevox
USER jukevox
ENTRYPOINT ["dotnet", "Jukevox.Server.dll"]
