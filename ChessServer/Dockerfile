# -------- BUILD STAGE --------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app

# Copy project files
COPY *.csproj ./
RUN dotnet restore

# Copy source
COPY . ./
RUN dotnet publish -c Release -o out

# -------- RUNTIME STAGE --------
FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app

# Copy published output
COPY --from=build /app/out .

# Chess server port
EXPOSE 5000

# Run server
ENTRYPOINT ["dotnet", "ChessServer.dll"]
