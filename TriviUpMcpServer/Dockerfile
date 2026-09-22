# Contexto de build: la raíz del repo (para poder copiar el proyecto compartido
# TriviUpMcp.Core, hermano de este). El servicio en Railway debe apuntar su
# "root directory" a la raíz del repo, no a esta carpeta.

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY TriviUpMcp.Core/TriviUpMcp.Core.csproj TriviUpMcp.Core/
COPY TriviUpMcpServer/TriviUpMcpServer.csproj TriviUpMcpServer/
RUN dotnet restore TriviUpMcpServer/TriviUpMcpServer.csproj

COPY TriviUpMcp.Core/ TriviUpMcp.Core/
COPY TriviUpMcpServer/ TriviUpMcpServer/

FROM build AS publish
RUN dotnet publish TriviUpMcpServer/TriviUpMcpServer.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

COPY --from=publish /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "TriviUpMcpServer.dll"]
