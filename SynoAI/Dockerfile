FROM mcr.microsoft.com/dotnet/aspnet:8.0-focal AS base
RUN apt-get update && apt-get install -y libgdiplus
WORKDIR /app
EXPOSE 80
EXPOSE 443

#FROM mcr.microsoft.com/dotnet/sdk:5.0 AS build
#Temporary fix for NuGet certificate bug
FROM mcr.microsoft.com/dotnet/sdk:8.0-focal AS build
WORKDIR /src
COPY ["euquiqsynoai.csproj", "./"]
RUN dotnet restore "euquiqsynoai.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "euquiqsynoai.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "euquiqsynoai.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "euquiqsynoai.dll"]