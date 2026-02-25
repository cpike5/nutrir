FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy solution and project files
COPY Nutrir.sln ./
COPY src/Nutrir.Core/Nutrir.Core.csproj src/Nutrir.Core/
COPY src/Nutrir.Infrastructure/Nutrir.Infrastructure.csproj src/Nutrir.Infrastructure/
COPY src/Nutrir.Web/Nutrir.Web.csproj src/Nutrir.Web/
COPY src/Nutrir.Cli/Nutrir.Cli.csproj src/Nutrir.Cli/

# Restore
RUN dotnet restore

# Copy everything and build
COPY . .
RUN dotnet publish src/Nutrir.Web/Nutrir.Web.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Nutrir.Web.dll"]
