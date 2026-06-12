# [INTENT]: Build multi-stage per WebAgency_BookingSystem.Api, ottimizzato per deploy su Railway (EU West).
# Stage `build` ripristina e pubblica; stage `final` contiene solo il runtime ASP.NET + i binari pubblicati
# (immagine finale minima, senza SDK). La porta è letta da $PORT a runtime in Program.cs (non bakata qui).

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

# WHY: la porta è gestita a runtime da Program.cs leggendo $PORT (iniettata da Railway). NON la bakiamo qui:
# `ENV ASPNETCORE_URLS=...:${PORT}` verrebbe valutata a build time (PORT assente) e l'app non ascolterebbe
# sulla porta runtime. L'immagine aspnet espone 8080 di default; in locale senza PORT Kestrel ascolta lì.
EXPOSE 8080

# WHY: esecuzione come utente non-root fornito dall'immagine (riduce la superficie in caso di compromissione).
USER app

ENTRYPOINT ["dotnet", "WebAgency_BookingSystem.Api.dll"]
