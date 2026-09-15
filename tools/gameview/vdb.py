"""Read a running GameView through the Havok Visual Debugger socket.

Start the runtime with -d and it listens on 25001.  What comes back is the
posed skeleton, one packet per bone segment per frame, which is the only
channel that reports what the behaviour actually evaluated: the Behaviors
viewer draws through the debug display rather than sending any text, and
ObjectInspection exposes the physics bodies, not the graph.

Three things the server insists on, none of which it says:

  * it ignores every command until the client echoes the version packet back;
  * it consumes commands only up to an ACK (0xF0), so one must follow them;
  * it writes one step chunk and then waits, so each step needs its own ACK or
    the stream stops after a single frame.

Server-to-client packets are length-prefixed; client-to-server ones are not.
"""
import collections
import socket
import struct

HK_STEP = 0x00
HK_DISPLAY_LINE = 0x08
HK_VERSION_INFORMATION = 0x90
HK_REGISTER_PROCESS = 0xC0
HK_CREATE_PROCESS = 0xC2
COMMAND_ACK = 0xF0


class Debugger:
    def __init__(self, host='127.0.0.1', port=25001, timeout=0.5):
        self.sock = socket.create_connection((host, port), 5)
        self.sock.settimeout(timeout)
        self.buf = b''
        self.version = None
        self.viewers = {}

    def packets(self, seconds, ack=False):
        import time
        end = time.time() + seconds
        while time.time() < end:
            try:
                chunk = self.sock.recv(262144)
            except socket.timeout:
                continue
            if not chunk:
                return
            self.buf += chunk
            i = 0
            while i + 4 <= len(self.buf):
                n = struct.unpack_from('<I', self.buf, i)[0]
                if n > 8_000_000 or i + 4 + n > len(self.buf):
                    break
                p, i = self.buf[i + 4:i + 4 + n], i + 4 + n
                if not p:
                    continue
                if ack and p[0] == HK_STEP:
                    self.sock.sendall(bytes([COMMAND_ACK]))
                yield p
            self.buf = self.buf[i:]

    def handshake(self, seconds=3):
        """Collect the version packet and the viewer list, then echo the
        version back -- until it arrives the server answers nothing."""
        for p in self.packets(seconds):
            if p[0] == HK_VERSION_INFORMATION:
                self.version = p
            elif p[0] == HK_REGISTER_PROCESS:
                n = struct.unpack_from('<H', p, 5)[0]
                self.viewers[p[7:7 + n].decode()] = struct.unpack_from('<I', p, 1)[0]
        self.sock.sendall(self.version)
        return self.viewers

    def enable(self, *names):
        for name in names:
            self.sock.sendall(struct.pack('<BI', HK_CREATE_PROCESS, self.viewers[name]))
        self.sock.sendall(bytes([COMMAND_ACK]))

    def poses(self, seconds):
        """One list of (from, to) segments per frame, in world space."""
        frame, out = [], []
        for p in self.packets(seconds, ack=True):
            if p[0] == HK_DISPLAY_LINE:
                frame.append(struct.unpack_from('<6f', p, 1))
            elif p[0] == HK_STEP and frame:
                out.append(frame)
                frame = []
        return out


def centroid(frame):
    return tuple(sum(seg[i] for seg in frame) / len(frame) for i in range(3))


if __name__ == '__main__':
    import sys
    d = Debugger()
    d.handshake()
    d.enable('Debug Display', 'Behaviors')
    frames = d.poses(float(sys.argv[1]) if len(sys.argv) > 1 else 6)
    print(f'{len(frames)} frames, {len(frames[0]) if frames else 0} segments each')
    if frames:
        print('centroid', tuple(round(v, 4) for v in centroid(frames[0])))
        moved = max(max(abs(a - b) for a, b in zip(s0, s1))
                    for s0, s1 in zip(frames[0], frames[-1]))
        print(f'max movement over the capture: {moved:.6f}')
