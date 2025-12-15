# Étape de build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copie des fichiers projet et restauration
COPY ["FusionPayProxy.csproj", "./"]
RUN dotnet restore "FusionPayProxy.csproj"

# Copie du reste et build
COPY . .
RUN dotnet build "FusionPayProxy.csproj" -c Release -o /app/build

# Publication
RUN dotnet publish "FusionPayProxy.csproj" -c Release -o /app/publish

# Étape d'exécution
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Installation des dépendances système pour SQLite
RUN apt-get update && apt-get install -y sqlite3 libsqlite3-dev && rm -rf /var/lib/apt/lists/*

EXPOSE 8080
ENV ASPNETCORE_URLS=http://*:8080
ENV ASPNETCORE_ENVIRONMENT=Production

# Création du dossier pour la base de données avec permissions
RUN mkdir -p /app/data && chmod 777 /app/data

COPY --from=build /app/publish .

# Point d'entrée
ENTRYPOINT ["dotnet", "FusionPayProxy.dll"]