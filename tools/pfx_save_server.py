# tools/make_pfx_sheets.js가 브라우저에서 만든 효과 시트(PNG, JSON)를 받아 tools/fx_out에 저장하는 작은 서버 (로컬 전용, 포트 5192)
import os
import re
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "fx_out")
os.makedirs(OUT, exist_ok=True)


class Handler(BaseHTTPRequestHandler):
    def cors(self):
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Methods", "POST, OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "Content-Type")

    def do_OPTIONS(self):
        self.send_response(204)
        self.cors()
        self.end_headers()

    def do_POST(self):
        name = self.path.lstrip("/")
        if not re.fullmatch(r"[a-z0-9_\-]+\.(png|json)", name):
            self.send_response(400)
            self.cors()
            self.end_headers()
            return
        data = self.rfile.read(int(self.headers.get("Content-Length", 0)))
        with open(os.path.join(OUT, name), "wb") as f:
            f.write(data)
        self.send_response(200)
        self.cors()
        self.end_headers()
        self.wfile.write(b"ok")

    def log_message(self, *args):
        pass


ThreadingHTTPServer(("127.0.0.1", 5192), Handler).serve_forever()
