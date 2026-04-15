FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY Tests/OpenAiResponses.Api/OpenAiResponses.Api.csproj Tests/OpenAiResponses.Api/
RUN dotnet restore Tests/OpenAiResponses.Api/OpenAiResponses.Api.csproj

COPY LLM/ LLM/
COPY TestData/ TestData/
COPY Tests/OpenAiResponses.Api/ Tests/OpenAiResponses.Api/

WORKDIR /src/Tests/OpenAiResponses.Api
RUN dotnet publish OpenAiResponses.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

RUN apt-get update \
	&& apt-get install -y --no-install-recommends fontconfig fonts-dejavu-core \
	&& rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .
COPY --from=build /src/LLM /app/LLM
COPY --from=build /src/TestData /app/TestData
COPY --from=build /src/Tests/OpenAiResponses.Api/Templates /app/Tests/OpenAiResponses.Api/Templates

ENV HTTP_PORTS=8081
ENV REPOSITORY_ROOT=/app

EXPOSE 8081

ENTRYPOINT ["dotnet", "OpenAiResponses.Api.dll"]