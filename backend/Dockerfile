# Build + run the ASP.NET Core Web API inside a Docker image.
#
# Context is the REPO ROOT (set in docker-compose.yml), so this Dockerfile can
# reach both `backend/src` (the .NET solution) and `ml/` (the Python training
# scripts). The trained ONNX priority model is gitignored (*.onnx) and absent on
# a clean clone, so instead of COPYing a file that may not exist, we GENERATE it
# at build time from ml/train_model.py. If the model is already present it is
# reused as-is (the script overwrites it either way — see the last build stage).
#
# The API listens on 8080 (overridden by --urls at runtime in compose). The ML
# fallback (RuleBasedPriorityPredictor) still protects us if generation ever
# fails, so the container never hard-fails on a missing model.

# --- .NET build stage ---
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY backend/CustomerServiceApi.sln ./
COPY backend/src/CustomerService.Api/CustomerService.Api.csproj backend/src/CustomerService.Api/
COPY backend/src/CustomerService.Application/CustomerService.Application.csproj backend/src/CustomerService.Application/
COPY backend/src/CustomerService.Domain/CustomerService.Domain.csproj backend/src/CustomerService.Domain/
COPY backend/src/CustomerService.Infrastructure/CustomerService.Infrastructure.csproj backend/src/CustomerService.Infrastructure/
COPY backend/src/CustomerService.ML/CustomerService.ML.csproj backend/src/CustomerService.ML/
RUN dotnet restore backend/src/CustomerService.Api/CustomerService.Api.csproj

COPY backend/src/ ./backend/src/
WORKDIR /src/backend/src/CustomerService.Api
RUN dotnet publish -c Release -o /app/publish --no-restore

# --- ML model generation stage (Python) ---
# pandas/sklearn/skl2onnx are needed only to produce the .onnx artifact; the
# runtime stage does not ship Python.
FROM python:3.12-slim AS ml
WORKDIR /ml
COPY ml/requirements.txt ./
# Install CPU-only deps. Fail loudly if a pin is unavailable so the build
# doesn't silently skip model generation.
RUN pip install --no-cache-dir -r requirements.txt
COPY ml/ ./ml/
RUN python ml/train_model.py --output /model/priority_model.onnx \
    && test -s /model/priority_model.onnx \
    && echo "ONNX model generated: $(stat -c%s /model/priority_model.onnx) bytes"

# --- Runtime stage ---
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./
COPY --from=ml /model/priority_model.onnx ./ml/models/priority_model.onnx

ENV ASPNETCORE_ENVIRONMENT=Production
ENV Database__Provider=Sqlite
ENV ML__ModelPath=/app/ml/models/priority_model.onnx

EXPOSE 8080
ENTRYPOINT ["dotnet", "CustomerService.Api.dll"]
