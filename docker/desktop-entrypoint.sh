#!/bin/sh
set -eu

app_pid=
web_pid=
vnc_pid=
wm_pid=
xvfb_pid=
nginx_pid=
cleanup() {
  kill "${app_pid:-}" "${web_pid:-}" "${vnc_pid:-}" "${wm_pid:-}" "${xvfb_pid:-}" "${nginx_pid:-}" 2>/dev/null || true
  wait 2>/dev/null || true
}
trap cleanup EXIT INT TERM

Xvfb :0 -screen 0 1600x1000x24 -ac +extension GLX +render -noreset &
xvfb_pid=$!
attempt=0
until xdpyinfo -display :0 >/dev/null 2>&1; do
  attempt=$((attempt + 1))
  if [ "$attempt" -ge 50 ]; then
    echo "Xvfb failed to start" >&2
    exit 1
  fi
  sleep 0.2
done

fluxbox -display :0 >/tmp/fluxbox.log 2>&1 &
wm_pid=$!
x11vnc -display :0 -forever -shared -localhost -rfbport 5900 -nopw -quiet &
vnc_pid=$!
websockify 127.0.0.1:6081 127.0.0.1:5900 >/tmp/websockify.log 2>&1 &
web_pid=$!

dotnet MiniPdm.Desktop.dll &
app_pid=$!

nginx -g 'daemon off;' &
nginx_pid=$!

wait "$app_pid"
