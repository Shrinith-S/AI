# Chitra Studio API (.NET 10). Build context: ./backend
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Chitra.slnx ./
COPY src/Chitra.Domain/Chitra.Domain.csproj src/Chitra.Domain/
COPY src/Chitra.Application/Chitra.Application.csproj src/Chitra.Application/
COPY src/Chitra.Infrastructure/Chitra.Infrastructure.csproj src/Chitra.Infrastructure/
COPY src/Chitra.Api/Chitra.Api.csproj src/Chitra.Api/
RUN dotnet restore src/Chitra.Api/Chitra.Api.csproj
COPY src/ src/
RUN dotnet publish src/Chitra.Api/Chitra.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# FFmpeg for thumbnails, last-frame extraction and final video export (all local).
# Python + the official Kaggle client for the optional Kaggle GPU provider (credentials are mounted at runtime).
RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg python3 python3-venv \
    && python3 -m venv /opt/kaggle && /opt/kaggle/bin/pip install --no-cache-dir kaggle \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_ENVIRONMENT=Production \
    Urls=http://+:8080 \
    Storage__RootPath=/data/storage \
    Database__SqlitePath=/data/chitra.db \
    Studio__FfmpegPath=ffmpeg \
    Studio__KagglePythonPath=/opt/kaggle/bin/python
VOLUME ["/data"]
EXPOSE 8080
ENTRYPOINT ["dotnet", "Chitra.Api.dll"]
