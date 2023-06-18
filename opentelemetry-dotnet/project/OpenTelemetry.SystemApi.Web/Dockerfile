FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["OpenTelemetry.SystemApi.Web/OpenTelemetry.SystemApi.Web.csproj", "OpenTelemetry.SystemApi.Web/"]
RUN dotnet restore "OpenTelemetry.SystemApi.Web/OpenTelemetry.SystemApi.Web.csproj"
COPY . .
WORKDIR "/src/OpenTelemetry.SystemApi.Web"
RUN dotnet build "OpenTelemetry.SystemApi.Web.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "OpenTelemetry.SystemApi.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "OpenTelemetry.SystemApi.Web.dll"]
