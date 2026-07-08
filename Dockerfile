FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copiar csproj y restaurar dependencias
COPY BancoPreguntas.Core/BancoPreguntas.Core.csproj       BancoPreguntas.Core/
COPY BancoPreguntas.Data/BancoPreguntas.Data.csproj       BancoPreguntas.Data/
COPY BancoPreguntas.Services/BancoPreguntas.Services.csproj BancoPreguntas.Services/
COPY BancoPreguntas.Web/BancoPreguntas.Web.csproj         BancoPreguntas.Web/
RUN dotnet restore BancoPreguntas.Web/BancoPreguntas.Web.csproj

# Copiar todo y publicar
COPY . .
RUN dotnet publish BancoPreguntas.Web/BancoPreguntas.Web.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Instalar libgdiplus para System.Drawing (renderizado de PNG en Linux)
RUN apt-get update && apt-get install -y libgdiplus libgssapi-krb5-2 && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:$PORT
ENTRYPOINT ["dotnet", "BancoPreguntas.Web.dll"]
