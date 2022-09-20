#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /
COPY ["KAST.Server/KAST.Server.csproj", "KAST.Server/"]
COPY ["KAST.Application/KAST.Application.csproj", "KAST.Application/"]
COPY ["KAST.Domain/KAST.Domain.csproj", "KAST.Domain/"]
COPY ["KAST.Infrastructure/KAST.Infrastructure.csproj", "KAST.Infrastructure/"]
RUN dotnet restore "KAST.Server/KAST.Server.csproj"
COPY . .
WORKDIR "/KAST.Server"
RUN dotnet build "KAST.Server.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "KAST.Server.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .


ENTRYPOINT ["dotnet", "KAST.Server.dll"]
