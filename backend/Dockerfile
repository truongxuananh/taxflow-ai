FROM mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim AS build

WORKDIR /src

COPY TaxFlow.Api.csproj ./

RUN dotnet restore TaxFlow.Api.csproj

COPY . .

RUN dotnet publish TaxFlow.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    -p:UseAppHost=false


FROM mcr.microsoft.com/dotnet/aspnet:8.0-bookworm-slim AS runtime

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
       curl \
       tesseract-ocr \
       tesseract-ocr-vie \
       poppler-utils \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

COPY --from=build /app/publish .

RUN mkdir -p /app/storage

EXPOSE 8080

ENTRYPOINT ["dotnet", "TaxFlow.Api.dll"]
