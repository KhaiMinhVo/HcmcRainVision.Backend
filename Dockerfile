# Sử dụng .NET SDK 9.0 để build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Khai báo biến đường dẫn để dễ quản lý
ARG PROJECT_PATH="khaiminhvo/hcmcrainvision.backend/HcmcRainVision.Backend-e4ecf8a377f366e508fdf61639174ae6065c8cb7"

# Copy file csproj từ thư mục sâu vào và restore dependencies
COPY ["${PROJECT_PATH}/HcmcRainVision.Backend.csproj", "HcmcRainVision.Backend/"]
RUN dotnet restore "HcmcRainVision.Backend/HcmcRainVision.Backend.csproj"

# Copy toàn bộ nội dung của thư mục dự án vào container
COPY ["${PROJECT_PATH}/.", "HcmcRainVision.Backend/"]

# Chuyển vào thư mục chứa code đã copy
WORKDIR "/src/HcmcRainVision.Backend"

# Build ứng dụng
RUN dotnet build "HcmcRainVision.Backend.csproj" -c Release -o /app/build

# Publish ứng dụng
FROM build AS publish
RUN dotnet publish "HcmcRainVision.Backend.csproj" -c Release -o /app/publish

# Tạo image final để chạy
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# Copy kết quả publish từ stage trước
COPY --from=publish /app/publish .

# Tạo thư mục lưu ảnh (quan trọng cho Worker lưu ảnh dựa trên RainScanningWorker.cs)
# Thư mục này nằm trong wwwroot để app có thể phục vụ ảnh tĩnh
RUN mkdir -p wwwroot/images/rain_logs

# Mở cổng 8080 (mặc định của container .NET 9)
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

# Chạy ứng dụng
ENTRYPOINT ["dotnet", "HcmcRainVision.Backend.dll"]