# Optional ComfyUI container.
#   GPU (NVIDIA):  TORCH_VARIANT=cu128, run with GPU passthrough (see docker-compose "gpu" profile)
#   CPU only:      TORCH_VARIANT=cpu and COMFYUI_ARGS=--cpu (very slow; small image models only)
FROM python:3.12-slim

ARG TORCH_VARIANT=cu128
# Pin to a release tag for reproducible builds, e.g. --build-arg COMFYUI_REF=v0.3.60
ARG COMFYUI_REF=master

RUN apt-get update \
    && apt-get install -y --no-install-recommends git ffmpeg libgl1 libglib2.0-0 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /opt
RUN git clone https://github.com/comfyanonymous/ComfyUI.git \
    && cd ComfyUI && git checkout "${COMFYUI_REF}"

WORKDIR /opt/ComfyUI
RUN pip install --no-cache-dir torch torchvision torchaudio --index-url "https://download.pytorch.org/whl/${TORCH_VARIANT}" \
    && pip install --no-cache-dir -r requirements.txt

ENV COMFYUI_ARGS=""
VOLUME ["/opt/ComfyUI/models", "/opt/ComfyUI/output", "/opt/ComfyUI/input"]
EXPOSE 8188
CMD ["sh", "-c", "python main.py --listen 0.0.0.0 --port 8188 ${COMFYUI_ARGS}"]
