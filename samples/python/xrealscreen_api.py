"""Minimal client for the XrealScreen local API (standard library only, Windows).

The running XrealScreen app listens on the named pipe \\\\.\\pipe\\XrealScreen.Api.
Messages are JSON-RPC 2.0, one JSON object per line (docs/sdk/local-api.md).

    python samples/python/xrealscreen_api.py              # status and saved workspaces
    python samples/python/xrealscreen_api.py poses        # 10 s of head poses (starts head look, mouse off)

This client is single-threaded: a synchronous Windows pipe handle cannot read and write at the
same time, so it sends a request and then reads lines until the matching response, keeping any
notifications (poses, status) that arrive in between.
"""

import json
import sys
import time
from collections import deque

PIPE = r"\\.\pipe\XrealScreen.Api"


class XrealScreenError(Exception):
    """An error response; .code is a JSON-RPC / XrealScreen error code (docs/sdk/local-api.md)."""

    def __init__(self, code, message):
        super().__init__(f"{code}: {message}")
        self.code = code


class XrealScreenApi:
    def __init__(self, client_name="python sample"):
        try:
            self._pipe = open(PIPE, "r+b", buffering=0)
        except FileNotFoundError:
            raise XrealScreenError(-32001, "XrealScreen is not running or its local API is off") from None
        self._buffer = b""
        self._next_id = 0
        self.notifications = deque()
        self.server = self.call("api.hello", {"clientName": client_name})

    def close(self):
        self._pipe.close()

    def __enter__(self):
        return self

    def __exit__(self, *_):
        self.close()

    def call(self, method, params=None):
        """Sends a request and returns its result; raises XrealScreenError on an error response."""
        self._next_id += 1
        request = {"jsonrpc": "2.0", "id": self._next_id, "method": method}
        if params is not None:
            request["params"] = params
        self._pipe.write((json.dumps(request) + "\n").encode("utf-8"))
        while True:
            message = self._read_message()
            if "method" in message:  # a notification that arrived before the response
                self.notifications.append(message)
            elif message.get("id") == self._next_id:
                if "error" in message:
                    raise XrealScreenError(message["error"]["code"], message["error"]["message"])
                return message["result"]

    def next_notification(self):
        """Returns the next notification (blocks until one arrives)."""
        return self.notifications.popleft() if self.notifications else self._read_message()

    def _read_message(self):
        while b"\n" not in self._buffer:
            chunk = self._pipe.read(65536)
            if not chunk:
                raise XrealScreenError(-32001, "XrealScreen closed the connection")
            self._buffer += chunk
        line, self._buffer = self._buffer.split(b"\n", 1)
        return json.loads(line)


def print_poses(api, seconds=10):
    started = False
    if api.call("status.get")["poseSource"] == "none":
        api.call("headLook.start")  # gives poses without a workspace; mouse output stays off
        started = True
        print("Started head look (mouse output off) to get poses.")
    api.call("pose.subscribe", {"rateHz": 10})
    end = time.monotonic() + seconds
    count = 0
    while time.monotonic() < end:
        message = api.next_notification()
        if message["method"] == "pose":
            pose = message["params"]
            count += 1
            print(f"[{pose['source']}] yaw {pose['yaw']:7.1f}  pitch {pose['pitch']:7.1f}  roll {pose['roll']:7.1f}")
    api.call("pose.unsubscribe")
    print(f"{count} poses in {seconds} s.")
    if started:
        api.call("headLook.stop")


def main():
    try:
        with XrealScreenApi() as api:
            print(f"XrealScreen {api.server['appVersion']}, API v{api.server['apiVersion']}")
            if sys.argv[1:2] == ["poses"]:
                print_poses(api)
                return 0
            status = api.call("status.get")
            print("Workspace:", status["workspace"]["state"])
            print("Head look:", "running" if status["headLook"]["running"] else "stopped")
            glasses = status["glasses"]
            print("Glasses:  ", glasses.get("displayName", "not connected as a display"))
            print("Saved workspaces:", ", ".join(api.call("workspace.list")["names"]) or "(none)")
            return 0
    except XrealScreenError as error:
        print(f"Error {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
