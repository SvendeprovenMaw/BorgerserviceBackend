FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY Backend.api/Backend.api.csproj Backend.api/
COPY JwtLibrary/JwtLibrary.csproj JwtLibrary/
COPY Openai.Library/Openai.Library.csproj Openai.Library/
RUN dotnet restore Backend.api/Backend.api.csproj

COPY Backend.api/ Backend.api/
COPY JwtLibrary/ JwtLibrary/
COPY Openai.Library/ Openai.Library/

WORKDIR /src/Backend.api
RUN dotnet publish Backend.api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080

EXPOSE 8080

ENTRYPOINT ["dotnet", "Backend.api.dll"]
