# Build the operator
FROM mcr.microsoft.com/dotnet/sdk:latest as build
WORKDIR /operator

COPY ./ ./
RUN dotnet publish -c Release -o out WirePact.Operator/WirePact.Operator.csproj

# The runner for the application
FROM mcr.microsoft.com/dotnet/aspnet:latest as final

ARG BUILD_VERSION
ARG COMMIT_SHA

LABEL org.opencontainers.image.source="https://github.com/WirePact/k8s-operator" \
    org.opencontainers.image.authors="cbuehler@rootd.ch" \
    org.opencontainers.image.url="https://github.com/WirePact/k8s-operator" \
    org.opencontainers.image.documentation="https://github.com/WirePact/k8s-operator/blob/main/README.md" \
    org.opencontainers.image.source="https://github.com/WirePact/k8s-operator/blob/main/WirePact.Operator/Dockerfile" \
    org.opencontainers.image.licenses="Apache-2.0" \
    org.opencontainers.image.version="${BUILD_VERSION}" \
    org.opencontainers.image.revision="${COMMIT_SHA}" \
    org.opencontainers.image.title="WirePact Kubernetes Operator" \
    org.opencontainers.image.description="Operator that manages WirePact mesh participants in Kubernetes fully automatically."

RUN addgroup k8s-operator && useradd -G k8s-operator operator-user

WORKDIR /operator
COPY --from=build /operator/out/ ./
RUN chown operator-user:k8s-operator -R .

USER operator-user

ENTRYPOINT [ "dotnet", "WirePact.Operator.dll" ]
