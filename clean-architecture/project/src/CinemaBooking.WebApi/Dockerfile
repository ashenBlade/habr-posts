FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["CinemaBooking.Domain/CinemaBooking.Domain.csproj", "CinemaBooking.Domain/"]
COPY ["CinemaBooking.Services/CinemaBooking.Services.csproj", "CinemaBooking.Services/"]
COPY ["CinemaBooking.WebApi/CinemaBooking.WebApi.csproj", "CinemaBooking.WebApi/"]
COPY ["CinemaBooking.Grpc/CinemaBooking.Grpc.csproj", "CinemaBooking.Grpc/"]
COPY ["CinemaBooking.Infrastructure/CinemaBooking.Infrastructure.csproj", "CinemaBooking.Infrastructure/"]

RUN dotnet restore "CinemaBooking.WebApi/CinemaBooking.WebApi.csproj"
COPY . .
WORKDIR "/src/CinemaBooking.WebApi"
RUN dotnet build "CinemaBooking.WebApi.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "CinemaBooking.WebApi.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "CinemaBooking.WebApi.dll"]
