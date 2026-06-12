# [INTENT]: Build multi-stage per WebAgency_BookingSystem.Api, ottimizzato per deploy su Railway (EU West).
# Stage `build` ripristina e pubblica; stage `final` contiene solo il runtime ASP.NET + i binari pubblicati
# (immagine finale minima, senza SDK). Railway inietta la porta via $PORT — Kestrel la legge a runtime.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# WHY: copiamo prima solution + .csproj e facciamo restore separato dal resto del codice.
# Così il layer di restore viene messo in cache da Docker e non si ripete a ogni modifica del codice.
COPY WebAgency_BookingSystem.slnx ./
COPY src/WebAgency_BookingSystem.Api/WebAgency_BookingSystem.Api.csproj src/WebAgency_BookingSystem.Api/
COPY src/WebAgency_BookingSystem.Core/WebAgency_BookingSystem.Core.csproj src/WebAgency_BookingSystem.Core/
COPY src/WebAgency_BookingSystem.Infrastructure/WebAgency_BookingSystem.Infrastructure.csproj src/WebAgency_BookingSystem.Infrastructure/
RUN dotnet restore src/WebAgency_BookingSystem.Api/WebAgency_BookingSystem.Api.csproj

COPY . .
RUN dotnet publish src/WebAgency_BookingSystem.Api/WebAgency_BookingSystem.Api.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# WHY: Railway non garantisce una porta fissa; ASPNETCORE_URLS con $PORT fa ascoltare Kestrel
# sulla porta assegnata. In locale (docker run senza PORT) si usa il default 8080.
ENV ASPNETCORE_URLS=http://+:${PORT:-8080}
EXPOSE 8080

ENTRYPOINT ["dotnet", "WebAgency_BookingSystem.Api.dll"]
