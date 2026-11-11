FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/AdmissionSystem.Domain/AdmissionSystem.Domain.csproj", "src/AdmissionSystem.Domain/"]
COPY ["src/AdmissionSystem.Application/AdmissionSystem.Application.csproj", "src/AdmissionSystem.Application/"]
COPY ["src/AdmissionSystem.Infrastructure/AdmissionSystem.Infrastructure.csproj", "src/AdmissionSystem.Infrastructure/"]
COPY ["src/AdmissionSystem.Api/AdmissionSystem.Api.csproj", "src/AdmissionSystem.Api/"]
RUN dotnet restore "src/AdmissionSystem.Api/AdmissionSystem.Api.csproj"

COPY src/ src/
RUN dotnet publish "src/AdmissionSystem.Api/AdmissionSystem.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "AdmissionSystem.Api.dll"]

