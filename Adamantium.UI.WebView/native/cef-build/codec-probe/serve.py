"""Serves the probe folder and writes what the page posts to results-<name>.json."""
import http.server
import sys

name = sys.argv[1]


class Handler(http.server.SimpleHTTPRequestHandler):
    def do_POST(self):
        length = int(self.headers["Content-Length"])
        with open(f"results-{name}.json", "wb") as file:
            file.write(self.rfile.read(length))
        self.send_response(204)
        self.end_headers()

    def log_message(self, format, *args):
        pass


http.server.ThreadingHTTPServer(("127.0.0.1", 8765), Handler).serve_forever()
