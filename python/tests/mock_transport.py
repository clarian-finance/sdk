"""In-memory HTTP transport for unit tests (no network)."""

from __future__ import annotations

import json
from dataclasses import dataclass, field
from typing import Any, Callable, Mapping, Optional
from urllib.parse import urlparse


@dataclass
class RecordedRequest:
    method: str
    url: str
    path: str
    headers: dict[str, str]
    body: Optional[bytes]


Handler = Callable[[RecordedRequest], tuple[int, bytes | str | dict[str, Any]]]


@dataclass
class MockTransport:
    """Records requests and returns canned responses via a handler."""

    handler: Handler
    calls: list[RecordedRequest] = field(default_factory=list)

    def __call__(
        self,
        method: str,
        url: str,
        headers: Mapping[str, str],
        body: Optional[bytes],
        timeout: float,
    ) -> tuple[int, bytes]:
        parsed = urlparse(url)
        path = parsed.path
        if parsed.query:
            path = f"{path}?{parsed.query}"
        # When base_url is a full gateway URL, path includes the gateway prefix.
        # Tests pass base_url like "https://mock" so path is just the API path.
        req = RecordedRequest(
            method=method,
            url=url,
            path=path,
            headers=dict(headers),
            body=body,
        )
        self.calls.append(req)
        status, payload = self.handler(req)
        if isinstance(payload, bytes):
            raw = payload
        elif isinstance(payload, str):
            raw = payload.encode("utf-8")
        else:
            raw = json.dumps(payload).encode("utf-8")
        return status, raw

    @property
    def last(self) -> RecordedRequest:
        if not self.calls:
            raise AssertionError("no requests recorded")
        return self.calls[-1]
