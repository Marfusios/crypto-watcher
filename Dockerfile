﻿FROM mcr.microsoft.com/dotnet/runtime:6.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["CryptoWatcher.csproj", "./"]
RUN dotnet restore "CryptoWatcher.csproj"
COPY . .
WORKDIR "/src/"
RUN dotnet build "CryptoWatcher.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "CryptoWatcher.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "CryptoWatcher.dll"]
