# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project files and restore
COPY TriviUpBackend.csproj ./
RUN dotnet restore TriviUpBackend.csproj

# Copy source
COPY . ./

# Stage 2: Publish
FROM build AS publish
WORKDIR /src
RUN dotnet publish TriviUpBackend.csproj -c Release -o /app/publish --no-restore

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Copy published files
COPY --from=publish /app/publish .

EXPOSE 5164
ENV ASPNETCORE_URLS=http://+:5164
ENTRYPOINT ["dotnet", "TriviUpBackend.dll"]
