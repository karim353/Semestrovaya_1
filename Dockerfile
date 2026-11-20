FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Framework/MiniHttpServer.Framework.csproj Framework/
COPY MiniTemplateEngine/MiniTemplateEngine.csproj MiniTemplateEngine/
COPY MyORMLibrary/MyORMLibrary.csproj MyORMLibrary/
COPY Lorcana/Lorcana.csproj Lorcana/
RUN dotnet restore Lorcana/Lorcana.csproj
COPY Lorcana/ Lorcana/
COPY Framework/ Framework/
COPY MiniTemplateEngine/ MiniTemplateEngine/
COPY MyORMLibrary/ MyORMLibrary/
RUN dotnet publish Lorcana/Lorcana.csproj -c Release --no-restore -o /out

FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=build /out .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Lorcana.App.dll"]
