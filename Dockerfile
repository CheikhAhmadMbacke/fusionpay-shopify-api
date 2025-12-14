# Étape 1 : Image de base
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

# Étape 2 : Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["FusionPayProxy.csproj", "."]
RUN dotnet restore "FusionPayProxy.csproj"
COPY . .
RUN dotnet build "FusionPayProxy.csproj" -c Release -o /app/build

# Étape 3 : Publish
FROM build AS publish
RUN dotnet publish "FusionPayProxy.csproj" -c Release -o /app/publish

# Étape 4 : Final
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "FusionPayProxy.dll"]