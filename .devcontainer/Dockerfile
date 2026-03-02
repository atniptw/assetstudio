FROM mcr.microsoft.com/dotnet/sdk:10.0

# Install additional development tools and dependencies
RUN apt-get update && apt-get install -y \
    git \
    curl \
    wget \
    p7zip-full \
    libbrotli-dev \
    libglib2.0-0 \
    libnss3 \
    libnspr4 \
    libdbus-1-3 \
    libatk1.0-0 \
    libatk-bridge2.0-0 \
    libcups2 \
    libdrm2 \
    libxcb1 \
    libxkbcommon0 \
    libatspi2.0-0 \
    libxcomposite1 \
    libxdamage1 \
    libxfixes3 \
    libxrandr2 \
    libgbm1 \
    libasound2t64 \
    libpango-1.0-0 \
    libcairo2 \
    libx11-6 \
    libx11-xcb1 \
    libxcb-dri3-0 \
    libxext6 \
    libxshmfence1 \
    libxss1 \
    libgtk-3-0 \
    fonts-liberation \
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
