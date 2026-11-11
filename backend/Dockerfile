# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ConsentManagement.slnx ./
COPY src/ src/
COPY tests/ tests/
RUN dotnet restore src/Consent.Api/Consent.Api.csproj
RUN dotnet publish src/Consent.Api/Consent.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true
COPY --from=build /app/publish .
# Image .NET chính thức có sẵn user không phải root tên "app".
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "Consent.Api.dll"]
