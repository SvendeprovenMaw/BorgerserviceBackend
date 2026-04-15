FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY Tests/OpenAiResponses.Api/OpenAiResponses.Api.csproj Tests/OpenAiResponses.Api/
RUN dotnet restore Tests/OpenAiResponses.Api/OpenAiResponses.Api.csproj

COPY Tests/OpenAiResponses.Api/ Tests/OpenAiResponses.Api/

WORKDIR /src/Tests/OpenAiResponses.Api
RUN dotnet publish OpenAiResponses.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8081

EXPOSE 8081

ENTRYPOINT ["dotnet", "OpenAiResponses.Api.dll"]