# ==============================
# Build stage
# ==============================
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Install Node.js (needed for Tailwind)
RUN apt-get update && apt-get install -y curl && \
    curl -fsSL https://deb.nodesource.com/setup_20.x | bash - && \
    apt-get install -y nodejs

# Copy sln and csproj first (for layer caching)
COPY *.sln .
COPY RealEstateCRM/*.csproj ./RealEstateCRM/
RUN dotnet restore

# Copy everything else
COPY . .

# Go into the project folder
WORKDIR /src/RealEstateCRM

# Install npm packages & build Tailwind
RUN npm install
RUN npm run build

# Publish the app
RUN dotnet publish -c Release -o /app/publish

# ==============================
# Runtime stage
# ==============================
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Install pg_isready (postgres client)
USER root
RUN apt-get update \
  && apt-get install -y --no-install-recommends postgresql-client \
  && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_URLS=http://+:80
EXPOSE 80

# Copy published app from build stage
COPY --from=build /app/publish .

# Copy entrypoint script
COPY scripts/entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh

ENTRYPOINT ["/entrypoint.sh"]
