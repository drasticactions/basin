#!/bin/sh
# The far side of a Tarn session, run on the Linux box that has the
# applications: scripts/tarn-relay.py pairs each connection waypipe server
# opens with a head connection on TCP 9800, and websocat turns a WebSocket on
# 9801 into one of those for the browser head. A relay and websocat left from
# an earlier run are stopped first, so starting it twice does not fail on the
# ports. Needs waypipe, python3 and websocat.
#
#   scripts/tarn-server.sh [APP...]      # foot by default
#   TARN_WAYPIPE_ARGS="--video h264" scripts/tarn-server.sh weston-simple-egl
export LANG=C.UTF-8
here=$(cd "$(dirname "$0")" && pwd)
pkill -f 'tarn-relay.py --port 9800' && echo "tarn-server: stopped the previous relay" >&2
pkill -f 'ws-l:0.0.0.0:9801' && echo "tarn-server: stopped the previous websocat" >&2
while ss -ltn 2>/dev/null | grep -qE ':980[01] '; do sleep 0.1; done
websocat -E --binary ws-l:0.0.0.0:9801 tcp:127.0.0.1:9800 &
relay=$!
trap 'kill $relay 2>/dev/null' EXIT
python3 "$here/tarn-relay.py" --port 9800 "$@"
