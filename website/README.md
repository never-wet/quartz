# Quartz download website

This folder contains the dependency-free Quartz product and download homepage.

## Preview locally

From the repository root:

```powershell
python -m http.server 8080
```

Then open `http://localhost:8080/website/`.

The download links point to the GitHub tags page so visitors can choose an available Quartz version. When a stable installer asset is uploaded, these links can be changed to a release page or direct installer asset URL.

No build step or external web dependency is required.
