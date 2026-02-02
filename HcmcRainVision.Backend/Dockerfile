# Sử dụng .NET SDK 9.0 để build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy file csproj và restore dependencies
COPY ["HcmcRainVision.Backend.csproj", "./"]
RUN dotnet restore "HcmcRainVision.Backend.csproj"

# Copy toàn bộ source code và build
COPY . .
WORKDIR "/src/."
RUN dotnet build "HcmcRainVision.Backend.csproj" -c Release -o /app/build

# Publish ứng dụng
FROM build AS publish
RUN dotnet publish "HcmcRainVision.Backend.csproj" -c Release -o /app/publish

# Tạo image final để chạy
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Tạo thư mục lưu ảnh (quan trọng cho Worker lưu ảnh)
# Dựa trên đường dẫn trong RainScanningWorker.cs
RUN mkdir -p wwwroot/images/rain_logs

# Mở cổng 8080 (mặc định của container .NET 9)
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

# Chạy ứng dụng
ENTRYPOINT ["dotnet", "HcmcRainVision.Backend.dll"]
