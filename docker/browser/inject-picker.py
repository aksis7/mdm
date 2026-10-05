#!/usr/bin/env python3
from pathlib import Path
import sys

target = Path(sys.argv[1])
html = target.read_text(encoding="utf-8")
marker = "<!-- Mini-PDM folder picker extension -->"
if marker not in html:
    if "</head>" not in html:
        raise SystemExit(f"Could not find </head> in {target}")
    extension = '''<!-- Mini-PDM folder picker extension -->
<link rel="stylesheet" href="/pdm-picker.css">
<script defer src="/pdm-picker.js"></script>
'''
    html = html.replace("</head>", extension + "</head>", 1)
    target.write_text(html, encoding="utf-8")
