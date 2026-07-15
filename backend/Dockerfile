# Build + run the ASP.NET Core Web API.
# Uses the SQLite provider by default (set Database__Provider=SqlServer in
# the environment to use SQL Server instead). The ONNX model is copied in so
# the AI priority prediction works inside the container.

# --- Build stage ---
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution + project files for efficient layer caching.
COPY CustomerServiceApi.sln ./
COPY src/CustomerService.Api/CustomerService.Api.csproj src/CustomerService.Api/
COPY src/CustomerService.Application/CustomerService.Application.csproj src/CustomerService.Application/
COPY src/CustomerService.Domain/CustomerService.Domain.csproj src/CustomerService.Domain/
COPY src/CustomerService.Infrastructure/CustomerService.Infrastructure.csproj src/CustomerService.Infrastructure/
COPY src/CustomerService.ML/CustomerService.ML.csproj src/CustomerService.ML/
RUN dotnet restore src/CustomerService.Api/CustomerService.Api.csproj

# Copy the rest and publish.
COPY src/ ./src/
WORKDIR /src/src/CustomerService.Api
RUN dotnet publish -c Release -o /app/publish --no-restore

# --- Runtime stage ---
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Copy the published API.
COPY --from=build /app/publish ./

# Copy the trained ONNX model so AI prediction works in-container.
COPY ml/models/priority_model.onnx ./ml/models/priority_model.onnx

ENV ASPNETCORE_ENVIRONMENT=Production
ENV Database__Provider=Sqlite
ENV ML__ModelPath=/app/ml/models/priority_model.onnx

EXPOSE 8080
ENTRYPOINT ["dotnet", "CustomerService.Api.dll"]
