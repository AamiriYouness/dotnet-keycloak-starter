FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json .
COPY src/Keycloak.Starter.Web/Keycloak.Starter.Web.csproj src/Keycloak.Starter.Web/
RUN dotnet restore src/Keycloak.Starter.Web/Keycloak.Starter.Web.csproj
COPY src/ src/
RUN dotnet publish src/Keycloak.Starter.Web/Keycloak.Starter.Web.csproj \
    --no-restore -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.13 AS final
WORKDIR /app
RUN mkdir /app/authentication && chown $APP_UID:$APP_UID /app/authentication && chmod 700 /app/authentication
USER $APP_UID
EXPOSE 8443
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Keycloak.Starter.Web.dll"]
