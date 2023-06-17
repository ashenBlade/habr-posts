FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["OpenTelemetry.RecordSaver.Worker/OpenTelemetry.RecordSaver.Worker.csproj", "OpenTelemetry.RecordSaver.Worker/"]
RUN dotnet restore "OpenTelemetry.RecordSaver.Worker/OpenTelemetry.RecordSaver.Worker.csproj"
COPY . .
WORKDIR "/src/OpenTelemetry.RecordSaver.Worker"
RUN dotnet build "OpenTelemetry.RecordSaver.Worker.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "OpenTelemetry.RecordSaver.Worker.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "OpenTelemetry.RecordSaver.Worker.dll"]
