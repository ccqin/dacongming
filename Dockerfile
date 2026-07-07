ARG TARGET
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGET
WORKDIR /src
COPY ["src/Zhuiying.${TARGET}/Zhuiying.${TARGET}.csproj", "src/Zhuiying.${TARGET}/"]
COPY ["src/Zhuiying.Shared/Zhuiying.Shared.csproj", "src/Zhuiying.Shared/"]
RUN dotnet restore "src/Zhuiying.${TARGET}/Zhuiying.${TARGET}.csproj"
COPY . .
RUN dotnet publish "src/Zhuiying.${TARGET}/Zhuiying.${TARGET}.csproj" -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
ARG TARGET
ENV TARGET=${TARGET}
WORKDIR /app
COPY --from=build /app .
RUN mkdir -p /app/data
ENTRYPOINT dotnet Zhuiying.${TARGET}.dll
