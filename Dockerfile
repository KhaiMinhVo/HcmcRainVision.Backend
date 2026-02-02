FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["HcmcRainVision.Backend.csproj", "./"]
RUN dotnet restore "HcmcRainVision.Backend.csproj"
COPY . .
RUN dotnet build "HcmcRainVision.Backend.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "HcmcRainVision.Backend.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .
RUN mkdir -p wwwroot/images/rain_logs
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "HcmcRainVision.Backend.dll"]