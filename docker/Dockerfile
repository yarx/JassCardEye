# Toolchain for the GPU pods. It carries no project code: docker/start.sh clones the repository into
# the running pod and runs src/scripts/run_training.sh there.
#
# That split is deliberate. The image changes only when the toolchain changes, so it is built rarely
# and stays cached; the code changes every day and comes from git. It also means the exact same
# script runs here and on a laptop, which is what makes a run rehearsable before it costs anything.
FROM pytorch/pytorch:2.5.1-cuda12.4-cudnn9-runtime

ENV DEBIAN_FRONTEND=noninteractive \
    PYTHONUNBUFFERED=1 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    DOTNET_GENERATE_ASPNET_CERTIFICATE=false \
    DOTNET_ROOT=/usr/share/dotnet \
    PATH=/usr/share/dotnet:$PATH

RUN apt-get update && apt-get install -y --no-install-recommends \
      ca-certificates curl git \
      libicu-dev \
      libgl1 libglib2.0-0 libsm6 libxext6 libxrender1 libxcb1 \
 && rm -rf /var/lib/apt/lists/*
# Two separate needs, both invisible until something crashes:
#
# libicu - the .NET runtime aborts on startup without it (SIGABRT, exit 134) rather than reporting a
#   missing library, because it refuses to run without globalization data. The -dev package pulls
#   whichever versioned libicu the distribution ships, so this survives a newer base image.
#
# The X libraries - Ultralytics installs the desktop build of OpenCV, which links against them even
#   though nothing here ever opens a window. They are listed together because they surface one at a
#   time: libxcb first, then libGL, and each discovery costs a pod rental.
# The generator is a .NET project, so the SDK has to be here; the repository is built inside the pod
# rather than shipped as a binary, so a change to the generator needs no image rebuild.
# The closing `dotnet --info` proves the runtime actually starts rather than that the files merely
# unpacked - which is exactly the difference a missing ICU library makes. Failing during the build
# is far cheaper than failing on a rented GPU.
RUN curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
 && bash /tmp/dotnet-install.sh --channel 10.0 --install-dir /usr/share/dotnet \
 && rm /tmp/dotnet-install.sh \
 && dotnet --info

# Pinned to the version src/training/requirements.txt pins, so cloud and laptop results stay
# comparable.
RUN pip install --no-cache-dir ultralytics==8.3.155

# azcopy, because the results have to leave this pod on their own - nothing waits outside to pull
# them off it. It moves a folder of weights far faster than the Azure CLI and brings no Python
# dependencies with it, which is what keeps this image about the toolchain.
#
# The closing `azcopy --version` proves the binary actually runs rather than that the tarball merely
# unpacked - the same reason `dotnet --info` is above. Failing during the build is far cheaper than
# failing on a rented GPU with three hours of results still on its disk.
#
# Pinned to an exact release rather than aka.ms/downloadazcopy-v10-linux, which always resolves to
# the newest one. A new azcopy that renamed a flag would pass the version check below and then fail
# while uploading - on the rented GPU, with the results still on its disk. Bump the version here
# deliberately; the one ARG below feeds both the download URL and the unpacked folder name.
ARG AZCOPY_VERSION=10.32.7
RUN curl -sSL "https://github.com/Azure/azure-storage-azcopy/releases/download/v${AZCOPY_VERSION}/azcopy_linux_amd64_${AZCOPY_VERSION}.tar.gz" -o /tmp/azcopy.tar.gz \
 && tar -xzf /tmp/azcopy.tar.gz -C /tmp \
 && mv "/tmp/azcopy_linux_amd64_${AZCOPY_VERSION}/azcopy" /usr/local/bin/azcopy \
 && chmod +x /usr/local/bin/azcopy \
 && rm -rf /tmp/azcopy.tar.gz "/tmp/azcopy_linux_amd64_${AZCOPY_VERSION}" \
 && azcopy --version

COPY docker/start.sh /start.sh
RUN chmod +x /start.sh

CMD ["/start.sh"]
