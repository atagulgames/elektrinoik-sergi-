FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ESergi.Api/ESergi.Api.csproj ESergi.Api/
RUN dotnet restore ESergi.Api/ESergi.Api.csproj
COPY ESergi.Api/ ESergi.Api/
RUN dotnet publish ESergi.Api/ESergi.Api.csproj -c Release -o /app/publish --no-restore
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
ENV ASPNETCORE_URLS=http://0.0.0.0:10000
EXPOSE 10000
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ESergi.Api.dll"]
