FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["OpenTelemetry.Web/OpenTelemetry.Web.csproj", "OpenTelemetry.Web/"]
RUN dotnet restore "OpenTelemetry.Web/OpenTelemetry.Web.csproj"
COPY . .
WORKDIR "/src/OpenTelemetry.Web"
RUN dotnet build "OpenTelemetry.Web.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "OpenTelemetry.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "OpenTelemetry.Web.dll"]
