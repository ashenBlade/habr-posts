FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["OpenTelemetry.TemperatureApi.Web/OpenTelemetry.TemperatureApi.Web.csproj", "OpenTelemetry.TemperatureApi.Web/"]
RUN dotnet restore "OpenTelemetry.TemperatureApi.Web/OpenTelemetry.TemperatureApi.Web.csproj"
COPY . .
WORKDIR "/src/OpenTelemetry.TemperatureApi.Web"
RUN dotnet build "OpenTelemetry.TemperatureApi.Web.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "OpenTelemetry.TemperatureApi.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "OpenTelemetry.TemperatureApi.Web.dll"]
