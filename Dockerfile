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
COPY Jukevox.slnx ./
COPY src/JukeVox.Server/Jukevox.Server.csproj src/JukeVox.Server/
COPY tests/JukeVox.Server.Tests/Jukevox.Server.Tests.csproj tests/JukeVox.Server.Tests/
RUN dotnet restore
COPY src/ src/
COPY tests/ tests/
COPY --from=frontend-build /app/src/Jukevox.Server/wwwroot/ src/JukeVox.Server/wwwroot/
RUN dotnet publish src/JukeVox.Server/Jukevox.Server.csproj -c Release -o /app/publish

# Stage 3 — Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
WORKDIR /app
COPY --from=backend-build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "Jukevox.Server.dll"]
