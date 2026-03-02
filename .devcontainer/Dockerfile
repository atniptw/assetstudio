FROM mcr.microsoft.com/dotnet/sdk:10.0

# Install additional development tools and dependencies
RUN apt-get update && apt-get install -y \
    git \
    curl \
    wget \
    p7zip-full \
    libbrotli-dev \
    build-essential \
    cmake \
    pkg-config \
    && rm -rf /var/lib/apt/lists/*

# Create vscode user
RUN useradd -m -s /bin/bash vscode && \
    mkdir -p /home/vscode/.vscode-server/extensions && \
    chown -R vscode:vscode /home/vscode

# Set working directory
WORKDIR /workspace

# Switch to vscode user
USER vscode

# Install .NET global tools if needed
RUN dotnet tool install -g dotnet-format || true

ENV PATH="${PATH}:/home/vscode/.dotnet/tools"
