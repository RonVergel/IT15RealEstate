# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# copy csproj and restore (improves cache)
COPY *.sln .
COPY RealEstateCRM/*.csproj ./RealEstateCRM/
RUN dotnet restore

# copy everything and publish
COPY . .
WORKDIR /src/RealEstateCRM
RUN dotnet publish -c Release -o /app/publish

# Runtime stage (updated to include pg_isready and entrypoint)
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# install pg_isready (postgres client) so entrypoint can wait for DB
USER root
RUN apt-get update \
  && apt-get install -y --no-install-recommends postgresql-client \
  && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_URLS=http://+:80
EXPOSE 80

# copy published app
COPY --from=build /app/publish .

# copy entrypoint and make executable
COPY scripts/entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh

ENTRYPOINT ["/entrypoint.sh"]