namespace Basin.Video.WebCodecs;

internal static class WebCodecsModule
{
    internal const string Name = "basin-webcodecs";

    internal const string Source = """
        const runtime = await globalThis.getDotnetRuntime(0);
        const heap = () => runtime.Module.HEAPU8;
        const sessions = new Map();
        let nextId = 1;

        export function available() {
            return typeof VideoDecoder !== "undefined" && typeof EncodedVideoChunk !== "undefined";
        }

        export async function supports(codec) {
            try {
                const result = await VideoDecoder.isConfigSupported({ codec });
                return !!result.supported;
            } catch {
                return false;
            }
        }

        export function create(codec, width, height, bgra, onOutput) {
            const id = nextId++;
            const state = { pending: [], timestamp: 0, decoder: null };
            const format = bgra ? "BGRA" : "RGBA";
            state.decoder = new VideoDecoder({
                output: (frame) => {
                    const target = state.pending.shift();
                    if (target === undefined) {
                        frame.close();
                        return;
                    }
                    const [destination, stride] = target;
                    (async () => {
                        let landed = false;
                        try {
                            const visible = frame.visibleRect;
                            if (visible.width >= width && visible.height >= height) {
                                const view = new Uint8Array(heap().buffer, destination, stride * height);
                                await frame.copyTo(view, {
                                    rect: { x: visible.x, y: visible.y, width, height },
                                    layout: [{ offset: 0, stride }],
                                    format,
                                });
                                landed = true;
                            }
                        } catch (error) {
                            console.warn("basin-webcodecs: a frame did not copy", error);
                        } finally {
                            frame.close();
                        }
                        onOutput(id, landed);
                    })();
                },
                error: (error) => {
                    console.warn("basin-webcodecs: the decoder failed", error);
                    while (state.pending.length > 0) {
                        state.pending.shift();
                        onOutput(id, false);
                    }
                },
            });
            state.decoder.configure({ codec, optimizeForLatency: true });
            sessions.set(id, state);
            return id;
        }

        export function decode(id, packet, length, key, destination, stride) {
            const state = sessions.get(id);
            if (state === undefined || state.decoder.state !== "configured") {
                return false;
            }
            state.timestamp += 16667;
            const chunk = new EncodedVideoChunk({
                type: key ? "key" : "delta",
                timestamp: state.timestamp,
                data: new Uint8Array(heap().buffer, packet, length),
            });
            state.pending.push([destination, stride]);
            try {
                state.decoder.decode(chunk);
                return true;
            } catch (error) {
                state.pending.pop();
                console.warn("basin-webcodecs: a chunk was refused", error);
                return false;
            }
        }

        export function close(id) {
            const state = sessions.get(id);
            if (state === undefined) {
                return;
            }
            sessions.delete(id);
            try {
                state.decoder.close();
            } catch {
            }
        }
        """;
}
