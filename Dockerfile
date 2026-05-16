FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

# Install Android SDK
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
        android-sdk \
        android-sdk-build-tools \
        openjdk-17-jdk-headless \
        && rm -rf /var/lib/apt/lists/*

ENV ANDROID_HOME=/usr/lib/android-sdk
ENV ANDROID_SDK_ROOT=/usr/lib/android-sdk

# Install MAUI Android workload
RUN dotnet workload install maui-android

WORKDIR /workspace
COPY . .

RUN dotnet restore && \
    dotnet publish -f net8.0-android -c Release

CMD ["cp", "bin/Release/net8.0-android/publish/*.apk", "/output/"]
