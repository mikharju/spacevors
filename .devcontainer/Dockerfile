FROM docker/sandbox-templates:opencode

USER root

# Base development tools
RUN apt-get update && apt-get install -y \
    git \
    curl \
    wget \
    unzip \
    zip \
    build-essential \
    pkg-config \
    ripgrep \
    fd-find \
    jq \
    tree \
    less \
    vim \
    clang \
    clang-format \
    cmake \
    ninja-build \
    gdb \
    ca-certificates \
    && rm -rf /var/lib/apt/lists/*

# Install Microsoft package repository
RUN wget https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb \
    && dpkg -i packages-microsoft-prod.deb \
    && rm packages-microsoft-prod.deb

# Install .NET 10 SDK
RUN apt-get update && apt-get install -y \
    dotnet-sdk-10.0 \
    && rm -rf /var/lib/apt/lists/*

# Useful .NET tools
RUN dotnet tool install --global dotnet-format

ENV PATH="${PATH}:/root/.dotnet/tools"

# Return to sandbox user
USER agent

WORKDIR /workspace