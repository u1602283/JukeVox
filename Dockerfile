# Stage 1 — Build frontend
FROM node:22-alpine AS frontend-build
WORKDIR /app/src/JukeVox.Client
COPY src/JukeVox.Client/package.json src/JukeVox.Client/package-lock.json ./
RUN npm ci
COPY src/JukeVox.Client/ ./
RUN npm run build

# Stage 2 — Build backend
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS backend-build
WORKDIR /app
COPY src/JukeVox.Server/JukeVox.Server.csproj src/JukeVox.Server/
RUN dotnet restore src/JukeVox.Server/JukeVox.Server.csproj
COPY src/ src/
COPY --from=frontend-build /app/src/JukeVox.Server/wwwroot/ src/JukeVox.Server/wwwroot/
RUN dotnet publish src/JukeVox.Server/JukeVox.Server.csproj -c Release -o /app/publish --no-restore

# Stage 3 — Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
WORKDIR /app
COPY --from=backend-build /app/publish .
EXPOSE 8080
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true
ENV ASPNETCORE_URLS=http://+:8080
RUN apk add --no-cache words && \
    addgroup -g 10000 -S jukevox && adduser -u 10000 -S jukevox -G jukevox
USER jukevox
ENTRYPOINT ["dotnet", "JukeVox.Server.dll"]
