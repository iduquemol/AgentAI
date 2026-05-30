FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY ConversationalAgent.Domain/ConversationalAgent.Domain.csproj             ConversationalAgent.Domain/
COPY ConversationalAgent.Application/ConversationalAgent.Application.csproj   ConversationalAgent.Application/
COPY ConversationalAgent.Infrastructure/ConversationalAgent.Infrastructure.csproj ConversationalAgent.Infrastructure/
COPY ConversationalAgent.Api/ConversationalAgent.Api.csproj                   ConversationalAgent.Api/

RUN dotnet restore ConversationalAgent.Api/ConversationalAgent.Api.csproj

COPY . .

RUN dotnet publish ConversationalAgent.Api/ConversationalAgent.Api.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "ConversationalAgent.Api.dll"]
