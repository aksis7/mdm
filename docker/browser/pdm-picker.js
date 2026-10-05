(() => {
  "use strict";

  const maxFiles = 1000;
  const maxFileBytes = 8 * 1024 * 1024;
  const maxRequestBytes = 64 * 1024 * 1024;
  const acceptedExtensions = new Set([".a3d", ".m3d"]);
  const pendingTimeoutMs = 10000;
  const encoder = new TextEncoder();
  let activeRequest = null;
  let uploading = false;
  let pollAbortController = null;
  let pollGeneration = 0;

  const input = document.createElement("input");
  input.type = "file";
  input.multiple = true;
  const supportsDirectorySelection = "webkitdirectory" in input;
  if (supportsDirectorySelection) {
    input.setAttribute("webkitdirectory", "");
    input.setAttribute("directory", "");
  }
  input.accept = ".a3d,.m3d";
  input.hidden = true;
  document.body.append(input);

  const overlay = document.createElement("section");
  overlay.id = "pdm-picker-overlay";
  overlay.hidden = true;
  overlay.setAttribute("role", "dialog");
  overlay.setAttribute("aria-modal", "true");
  overlay.setAttribute("aria-labelledby", "pdm-picker-title");
  overlay.innerHTML = `
    <div id="pdm-picker-panel">
      <h1 id="pdm-picker-title">Выбор папки</h1>
      <p id="pdm-picker-instructions"></p>
      <p id="pdm-picker-status" aria-live="polite"></p>
      <div id="pdm-picker-actions">
        <button id="pdm-picker-choose" type="button">Выбрать папку на компьютере</button>
        <button id="pdm-picker-cancel" class="secondary" type="button">Отмена</button>
      </div>
    </div>`;
  function placeOverlay() {
    const fullscreenElement = document.fullscreenElement || document.webkitFullscreenElement;
    const parent = fullscreenElement || document.body;
    if (overlay.parentElement !== parent) parent.append(overlay);
  }
  placeOverlay();

  const status = overlay.querySelector("#pdm-picker-status");
  overlay.querySelector("#pdm-picker-instructions").textContent = supportsDirectorySelection
    ? "Чтобы выбрать папку на этом компьютере, нажмите кнопку ниже. Будут отправлены файлы .a3d и .m3d, в том числе во вложенных папках. Отчёт появится в приложении."
    : "Браузер не поддерживает выбор папки целиком. Нажмите кнопку ниже и отметьте CAD-файлы .a3d и .m3d в диалоге выбора файлов. Отчёт появится в приложении.";
  const chooseButton = overlay.querySelector("#pdm-picker-choose");
  const cancelButton = overlay.querySelector("#pdm-picker-cancel");

  function basename(path) {
    return path.replaceAll("\\", "/").split("/").pop() || "";
  }

  function extension(name) {
    const dot = name.lastIndexOf(".");
    return dot < 0 ? "" : name.slice(dot).toLowerCase();
  }

  function showError(message) {
    status.textContent = message;
    chooseButton.disabled = false;
    cancelButton.disabled = false;
  }

  function showRequest(request) {
    if (activeRequest?.requestId === request.requestId && activeRequest?.nonce === request.nonce) return;
    activeRequest = request;
    uploading = false;
    status.textContent = "";
    chooseButton.disabled = false;
    cancelButton.disabled = false;
    overlay.hidden = false;

    // The request arrives asynchronously from Avalonia, so browsers correctly
    // reject a synthetic picker click. The visible button supplies user activation.
    if (navigator.userActivation?.isActive) input.click();
  }

  function setBusy(value) {
    uploading = value;
    chooseButton.disabled = value;
    cancelButton.disabled = value;
  }

  async function cancelRequest() {
    const request = activeRequest;
    if (!request || uploading) return;
    setBusy(true);
    try {
      const response = await fetch(`/pdm-picker/${encodeURIComponent(request.requestId)}/cancel`, {
        method: "POST",
        cache: "no-store",
        headers: { "X-Pdm-Picker-Token": request.nonce }
      });
      if (!response.ok && response.status !== 204) throw new Error(`Не удалось отменить выбор папки (HTTP ${response.status}).`);
      if (activeRequest?.nonce === request.nonce) {
        activeRequest = null;
        setBusy(false);
        overlay.hidden = true;
      }
    } catch (error) {
      if (activeRequest?.nonce === request.nonce) {
        showError(error instanceof Error ? error.message : "Не удалось отменить выбор папки.");
        setBusy(false);
      }
    }
  }

  function validateFiles(fileList) {
    const files = Array.from(fileList || []).filter(file => acceptedExtensions.has(extension(file.name)));
    if (files.length === 0) throw new Error("В выбранной папке не найдены файлы .a3d или .m3d.");
    if (files.length > maxFiles) throw new Error(`Найдено ${files.length} CAD-файлов; максимум за один импорт — ${maxFiles}.`);

    const names = new Set();
    let estimatedSize = 2048;
    for (const file of files) {
      const name = basename(file.name);
      const key = name;
      if (names.has(key)) throw new Error(`Имя «${name}» встречается несколько раз. Уберите одинаковые имена CAD-файлов и выберите папку снова.`);
      names.add(key);
      if (file.size > maxFileBytes) throw new Error(`Файл «${name}» больше максимального размера 8 МиБ.`);
      estimatedSize += file.size + 2048 + encoder.encode(name).length * 4;
      if (estimatedSize > maxRequestBytes) throw new Error("Размер файлов вместе со служебными данными превышает лимит запроса 64 МиБ.");
    }
    return files;
  }

  async function uploadFiles(fileList) {
    const request = activeRequest;
    if (!request || uploading) return;

    let files;
    try {
      files = validateFiles(fileList);
    } catch (error) {
      showError(error instanceof Error ? error.message : "Не удалось проверить выбранную папку.");
      input.value = "";
      return;
    }

    setBusy(true);
    status.textContent = `Передаём ${files.length} файлов…`;
    const body = new FormData();
    for (const file of files) body.append("files", file, basename(file.name));

    try {
      const response = await fetch(`/pdm-picker/${encodeURIComponent(request.requestId)}/files`, {
        method: "POST",
        cache: "no-store",
        headers: { "X-Pdm-Picker-Token": request.nonce },
        body
      });
      if (!response.ok && response.status !== 204) {
        let detail = `Не удалось передать файлы (HTTP ${response.status}).`;
        try {
          const payload = await response.json();
          if (typeof payload?.error === "string") detail = payload.error;
          else if (typeof payload?.title === "string") detail = payload.title;
        } catch { /* Keep the status-based message. */ }
        throw new Error(detail);
      }
      if (activeRequest?.nonce === request.nonce) {
        activeRequest = null;
        setBusy(false);
        overlay.hidden = true;
      }
    } catch (error) {
      if (activeRequest?.nonce === request.nonce) {
        showError(`${error instanceof Error ? error.message : "Не удалось передать файлы."} Можно выбрать папку повторно.`);
      }
    } finally {
      input.value = "";
      if (activeRequest?.nonce === request.nonce) setBusy(false);
    }
  }

  function delay(ms, signal) {
    return new Promise(resolve => {
      if (signal.aborted) return resolve();
      const timer = window.setTimeout(done, ms);
      function done() {
        window.clearTimeout(timer);
        signal.removeEventListener("abort", done);
        resolve();
      }
      signal.addEventListener("abort", done, { once: true });
    });
  }

  async function fetchPending(signal) {
    const controller = new AbortController();
    let timeoutId;
    let rejectOnAbort;
    const aborted = new Promise((_, reject) => { rejectOnAbort = reject; });
    const abortWithError = () => {
      controller.abort();
      rejectOnAbort(new Error("Pending picker request was aborted."));
    };
    signal.addEventListener("abort", abortWithError, { once: true });
    const timedOut = new Promise((_, reject) => {
      timeoutId = window.setTimeout(() => {
        controller.abort();
        reject(new Error("Pending picker request timed out."));
      }, pendingTimeoutMs);
    });
    const pending = (async () => {
      const response = await fetch("/pdm-picker/pending", {
        cache: "no-store",
        headers: { "Accept": "application/json" },
        signal: controller.signal
      });
      return { response, request: response.status === 200 ? await response.json() : null };
    })();
    try {
      return await Promise.race([pending, timedOut, aborted]);
    } finally {
      window.clearTimeout(timeoutId);
      signal.removeEventListener("abort", abortWithError);
    }
  }

  async function pollPending(controller, generation) {
    const signal = controller.signal;
    const current = () => !signal.aborted && generation === pollGeneration;
    while (current()) {
      try {
        const { response, request } = await fetchPending(signal);
        if (!current()) return;
        if (response.status === 200) {
          if (request && typeof request.requestId === "string" && typeof request.nonce === "string" && typeof request.expiresAt === "string") {
            if (uploading && activeRequest?.nonce !== request.nonce) {
              await delay(500, signal);
              continue;
            }
            if (Date.parse(request.expiresAt) > Date.now()) showRequest(request);
            else if (activeRequest?.nonce === request.nonce && !uploading) {
              activeRequest = null;
              overlay.hidden = true;
            }
          }
          await delay(500, signal);
        } else if (response.status === 204) {
          if (activeRequest && !uploading) {
            activeRequest = null;
            overlay.hidden = true;
          }
          await delay(500, signal);
        } else {
          await delay(1200, signal);
        }
      } catch {
        if (!current()) return;
        // A transient proxy restart or dropped poll must not discard the pending picker request.
        await delay(1200, signal);
      }
    }
  }

  function startPolling() {
    if (pollAbortController) return;
    pollAbortController = new AbortController();
    const controller = pollAbortController;
    const generation = ++pollGeneration;
    void pollPending(controller, generation).finally(() => {
      if (pollAbortController === controller) pollAbortController = null;
    });
  }

  function stopPolling() {
    if (!pollAbortController) return;
    ++pollGeneration;
    pollAbortController.abort();
    pollAbortController = null;
  }

  chooseButton.addEventListener("click", () => {
    if (!activeRequest || uploading) return;
    status.textContent = "";
    input.click();
  });
  cancelButton.addEventListener("click", cancelRequest);
  input.addEventListener("change", () => uploadFiles(input.files));
  document.addEventListener("fullscreenchange", placeOverlay);
  document.addEventListener("webkitfullscreenchange", placeOverlay);
  window.addEventListener("pagehide", stopPolling);
  window.addEventListener("pageshow", startPolling);
  startPolling();
})();
