# Quartz download website

This folder contains the dependency-free Quartz product and download homepage.

## Preview locally

From the repository root:

```powershell
python -m http.server 8080
```

Then open `http://localhost:8080/website/`.

The download links point to the GitHub `v2.0.0` release page. When a stable installer asset is uploaded, these links can be changed to the direct `Quartz-2.0.0-Setup.exe` asset URL.

No build step or external web dependency is required.
