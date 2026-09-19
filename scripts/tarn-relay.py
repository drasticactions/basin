#!/usr/bin/env python3
# The far side of a Tarn session: pairs each connection waypipe server opens
# with one connection a head opened, first come first served. waypipe server
# dials its socket once per Wayland client and a phone or a browser cannot be
# dialled, so the head keeps a spare connection waiting here and opens
# another each time one is taken. A head connection starts one waypipe
# server around APP when none is running; the server exits with the
# application, and the heads still waiting are closed with it.
#
#   scripts/tarn-relay.py [--port 9800] [--socket /tmp/tarn-relay.sock]
#                         [--waypipe-args "--video h264"] [APP...]
#
# TARN_WAYPIPE_ARGS is the same as --waypipe-args. --no-gpu is the default;
# "--video h264" (or vp9, av1) encodes the dmabufs a GPU client draws, which
# a head decodes with the decoder its platform has.
import argparse, asyncio, os, signal, sys

parser = argparse.ArgumentParser()
parser.add_argument("--port", type=int, default=9800)
parser.add_argument("--socket", default="/tmp/tarn-relay.sock")
parser.add_argument("--waypipe-args", default=os.environ.get("TARN_WAYPIPE_ARGS", "--no-gpu"),
                    help="what waypipe server takes before its socket: --no-gpu by default, or '--video h264' for a GPU box")
parser.add_argument("app", nargs="*", default=["foot"])
args = parser.parse_args()

# A stack rather than a queue: a head whose page went away can linger here
# until its bridge notices, and the connection a live head opened last is the
# one most likely to be listening.
heads: list = []
head_arrived = asyncio.Event()
server_process = None


def log(text):
    print(text, file=sys.stderr, flush=True)


async def pump(reader, writer, name):
    total = 0
    try:
        while True:
            data = await reader.read(65536)
            if not data:
                log(f"{name}: end of stream after {total} bytes")
                break
            if total == 0:
                log(f"{name}: first bytes {data[:16].hex()}")
            total += len(data)
            writer.write(data)
            await writer.drain()
    except (ConnectionError, asyncio.IncompleteReadError, OSError) as error:
        log(f"{name}: {error} after {total} bytes")
    finally:
        try:
            writer.close()
        except OSError:
            pass


class Head:
    def __init__(self, reader, writer):
        self.reader = reader
        self.writer = writer
        self.closed = False
        self.watcher = asyncio.create_task(self.watch())

    async def watch(self):
        # A head waiting for a client never speaks first: the server sends the
        # connection header. A byte here is a channel the head has already
        # given up on, closing behind it, so the connection is dropped rather
        # than paired with the next client.
        try:
            data = await self.reader.read(1)
        except (ConnectionError, OSError):
            data = b""
        self.closed = True
        self.writer.close()
        log("head connection closed while waiting" if not data else "head connection spoke while waiting; dropped")

    async def take(self):
        if not self.watcher.done():
            self.watcher.cancel()
            try:
                await self.watcher
            except asyncio.CancelledError:
                pass
        return not self.closed


async def start_server():
    global server_process
    if server_process is not None and server_process.returncode is None:
        return
    env = dict(os.environ, LANG="C.UTF-8")
    env.setdefault("XDG_RUNTIME_DIR", f"/run/user/{os.getuid()}")
    server_process = await asyncio.create_subprocess_exec(
        "waypipe", "--compress", "lz4", *args.waypipe_args.split(), "--socket", args.socket, "server", "--", *args.app, env=env)
    log(f"waypipe server started for {' '.join(args.app)}")
    asyncio.create_task(restart_when_exited(server_process))


async def restart_when_exited(process):
    # The server exits with its application, and that ends the session: every
    # head connection still waiting is closed, so the head reports the end and
    # its next Connect starts a fresh server.
    await process.wait()
    log(f"waypipe server exited with {process.returncode}")
    waiting = heads[:]
    heads.clear()
    for head in waiting:
        if await head.take():
            head.writer.close()
            log("closed a waiting head connection: the application exited")
    if heads:
        await start_server()


async def on_head(reader, writer):
    log("head connected, waiting for a client")
    heads.append(Head(reader, writer))
    head_arrived.set()
    await start_server()


async def on_server(reader, writer):
    while True:
        while not heads:
            head_arrived.clear()
            await head_arrived.wait()
        head = heads.pop()
        if await head.take():
            break
    log("paired a client with a waiting head connection")
    await asyncio.gather(pump(reader, head.writer, "server to head"), pump(head.reader, writer, "head to server"))
    log("pair closed")


async def main():
    if os.path.exists(args.socket):
        os.unlink(args.socket)
    await asyncio.start_unix_server(on_server, path=args.socket)
    await asyncio.start_server(on_head, host="0.0.0.0", port=args.port)
    log(f"relay: heads on tcp {args.port}, waypipe on {args.socket}")
    stop = asyncio.Event()
    for sig in (signal.SIGINT, signal.SIGTERM):
        asyncio.get_running_loop().add_signal_handler(sig, stop.set)
    await stop.wait()
    if server_process is not None and server_process.returncode is None:
        server_process.terminate()


asyncio.run(main())
