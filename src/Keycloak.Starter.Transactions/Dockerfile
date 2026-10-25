FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json .
COPY src/Keycloak.Starter.Transactions/Keycloak.Starter.Transactions.csproj src/Keycloak.Starter.Transactions/
COPY src/Keycloak.Starter.Authentication/Keycloak.Starter.Authentication.csproj src/Keycloak.Starter.Authentication/
RUN dotnet restore src/Keycloak.Starter.Transactions/Keycloak.Starter.Transactions.csproj
COPY src/ src/
RUN dotnet publish src/Keycloak.Starter.Transactions/Keycloak.Starter.Transactions.csproj \
    --no-restore -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.13 AS final
WORKDIR /app
USER $APP_UID
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Keycloak.Starter.Transactions.dll"]
